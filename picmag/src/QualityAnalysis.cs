using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TurboJpegWrapper;

namespace picmag
{
    public enum QualityFilterMode
    {
        Off,
        Warn,
        Strict
    }

    public enum QualityVerdict
    {
        Accept,
        Review,
        Reject,
        Error
    }

    public enum QualityReviewVerdict
    {
        Review,
        Reject
    }

    public enum QualityReviewAction
    {
        List,
        Delete,
        Interactive
    }

    public class QualityAssessmentResult
    {
        public string SourcePath { get; set; } = string.Empty;
        public string FilePath
        {
            get => SourcePath;
            set => SourcePath = value;
        }
        public string TargetRelativePath { get; set; } = string.Empty;
        public bool WasImported { get; set; }
        public QualityVerdict Verdict { get; set; }
        public double ClippedHighlightsRatio { get; set; }
        public double ClippedShadowsRatio { get; set; }
        public double Contrast { get; set; }
        public double Sharpness { get; set; }
        public string Reason { get; set; } = string.Empty;

        public string ToSummary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "verdict={0}, imported={1}, target={2}, highlights={3:P1}, shadows={4:P1}, contrast={5:F3}, sharpness={6:F1}, reason={7}",
                Verdict,
                WasImported,
                string.IsNullOrWhiteSpace(TargetRelativePath) ? "n/a" : TargetRelativePath,
                ClippedHighlightsRatio,
                ClippedShadowsRatio,
                Contrast,
                Sharpness,
                string.IsNullOrWhiteSpace(Reason) ? "n/a" : Reason);
        }
    }

    public static class QualityAnalyzer
    {
        private static int turboJpegAvailability;
        private static int turboJpegResolverInitialized;
        private static readonly object turboJpegProbeLock = new object();
        private static readonly int turboJpegMaxParallelism = GetTurboJpegMaxParallelism();
        private static readonly SemaphoreSlim turboJpegDecodeGate = turboJpegMaxParallelism > 0
            ? new SemaphoreSlim(turboJpegMaxParallelism, turboJpegMaxParallelism)
            : null;

        public static bool TryAnalyzeJpeg(string filePath, out QualityAssessmentResult result)
        {
            result = new QualityAssessmentResult { SourcePath = filePath, Verdict = QualityVerdict.Error, Reason = "analysis failed" };
            try
            {
                if (TryAnalyzeJpegWithTurboJpeg(filePath, out result))
                    return true;

                return TryAnalyzeJpegWithImageSharp(filePath, out result);
            }
            catch (Exception ex)
            {
                result.Reason = ex.Message;
                result.Verdict = QualityVerdict.Error;
                return false;
            }
        }

        private static bool TryAnalyzeJpegWithTurboJpeg(string filePath, out QualityAssessmentResult result)
        {
            result = new QualityAssessmentResult { SourcePath = filePath, Verdict = QualityVerdict.Error, Reason = "analysis failed" };

            if (!CanUseTurboJpeg())
                return false;

            var decodeGate = turboJpegDecodeGate;
            if (decodeGate != null)
                decodeGate.Wait();

            try
            {
                var jpegBytes = File.ReadAllBytes(filePath);
                if (!LooksLikeJpeg(jpegBytes))
                    return false;

                using var decompressor = new TJDecompressor();
                var image = decompressor.Decompress(jpegBytes, TJPixelFormats.TJPF_RGB, TJFlags.FASTDCT | TJFlags.FASTUPSAMPLE);
                Volatile.Write(ref turboJpegAvailability, 1);

                int width = image.Width;
                int height = image.Height;
                int rowBytes = image.RowBytes;
                int step = Math.Max(1, Math.Max(width, height) / 512);
                int sampleWidth = (width + step - 1) / step;
                int sampleHeight = (height + step - 1) / step;

                if (sampleWidth < 1 || sampleHeight < 1)
                {
                    result.Reason = "invalid image dimensions";
                    return false;
                }

                var luminance = new byte[sampleWidth * sampleHeight];
                int index = 0;
                double sum = 0;
                double sumSquares = 0;
                int highlights = 0;
                int shadows = 0;

                for (int y = 0; y < height; y += step)
                {
                    int rowStart = y * rowBytes;
                    for (int x = 0; x < width; x += step)
                    {
                        int pixelOffset = rowStart + (x * 3);
                        int valueInt = ((54 * image.Data[pixelOffset]) + (183 * image.Data[pixelOffset + 1]) + (19 * image.Data[pixelOffset + 2])) >> 8;
                        byte value = (byte)valueInt;
                        luminance[index++] = value;

                        sum += value;
                        sumSquares += value * value;
                        if (value <= 5)
                            shadows++;
                        if (value >= 250)
                            highlights++;
                    }
                }

                return TryBuildResult(filePath, luminance, sampleWidth, sampleHeight, index, sum, sumSquares, highlights, shadows, out result);
            }
            catch (DllNotFoundException)
            {
                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }
            catch (BadImageFormatException)
            {
                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }
            catch (TypeInitializationException)
            {
                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (decodeGate != null)
                    decodeGate.Release();
            }
        }

        private static bool CanUseTurboJpeg()
        {
            if (ShouldDisableTurboJpegByDefault())
            {
                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }

            var availability = Volatile.Read(ref turboJpegAvailability);
            if (availability > 0)
                return true;

            if (availability < 0)
                return false;

            lock (turboJpegProbeLock)
            {
                availability = Volatile.Read(ref turboJpegAvailability);
                if (availability != 0)
                    return availability > 0;

                EnsureTurboJpegResolverConfigured();

                if (TryLoadTurboJpegLibrary())
                {
                    Volatile.Write(ref turboJpegAvailability, 1);
                    return true;
                }

                Volatile.Write(ref turboJpegAvailability, -1);
                return false;
            }
        }

        private static bool ShouldDisableTurboJpegByDefault()
        {
            var overrideValue = Environment.GetEnvironmentVariable("PICMAG_USE_TURBOJPEG");
            if (!string.IsNullOrWhiteSpace(overrideValue))
            {
                if (IsEnabledValue(overrideValue))
                    return false;

                if (IsDisabledValue(overrideValue))
                    return true;
            }

            return false;
        }

        private static int GetTurboJpegMaxParallelism()
        {
            var overrideValue = Environment.GetEnvironmentVariable("PICMAG_TURBOJPEG_MAX_PARALLELISM");
            if (TryParsePositiveInt(overrideValue, out var configuredParallelism))
                return configuredParallelism;

            if (IsLinuxArmProcess())
                return 1;

            return 0;
        }

        private static bool TryParsePositiveInt(string value, out int parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return false;

            return parsed > 0;
        }

        private static bool IsLinuxArmProcess()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return false;

            return RuntimeInformation.ProcessArchitecture == Architecture.Arm
                || RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        }

        private static bool IsEnabledValue(string value)
        {
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisabledValue(string value)
        {
            return value.Equals("0", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("no", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureTurboJpegResolverConfigured()
        {
            if (Interlocked.CompareExchange(ref turboJpegResolverInitialized, 1, 0) != 0)
                return;

            NativeLibrary.SetDllImportResolver(typeof(TJDecompressor).Assembly, ResolveTurboJpegImport);
        }

        private static IntPtr ResolveTurboJpegImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "turbojpeg.dll", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            IntPtr handle;
            if (TryLoadTurboJpegLibrary(out handle))
                return handle;

            return IntPtr.Zero;
        }

        private static bool TryLoadTurboJpegLibrary()
        {
            IntPtr handle;
            if (TryLoadTurboJpegLibrary(out handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }

            return false;
        }

        private static bool LooksLikeJpeg(byte[] data)
        {
            if (data == null || data.Length < 4)
                return false;

            return data[0] == 0xFF
                && data[1] == 0xD8
                && data[data.Length - 2] == 0xFF
                && data[data.Length - 1] == 0xD9;
        }

        private static bool TryLoadTurboJpegLibrary(out IntPtr handle)
        {
            if (NativeLibrary.TryLoad("turbojpeg.dll", out handle))
                return true;

            if (NativeLibrary.TryLoad("turbojpeg", out handle))
                return true;

            if (NativeLibrary.TryLoad("libturbojpeg.so", out handle))
                return true;

            if (NativeLibrary.TryLoad("libturbojpeg.so.0", out handle))
                return true;

            if (NativeLibrary.TryLoad("libturbojpeg", out handle))
                return true;

            handle = IntPtr.Zero;

            return false;
        }

        private static bool TryAnalyzeJpegWithImageSharp(string filePath, out QualityAssessmentResult result)
        {
            result = new QualityAssessmentResult { SourcePath = filePath, Verdict = QualityVerdict.Error, Reason = "analysis failed" };
            try
            {
                using var image = Image.Load<Rgb24>(filePath);
                int step = Math.Max(1, Math.Max(image.Width, image.Height) / 512);
                int sampleWidth = (image.Width + step - 1) / step;
                int sampleHeight = (image.Height + step - 1) / step;

                if (sampleWidth < 1 || sampleHeight < 1)
                {
                    result.Reason = "invalid image dimensions";
                    return false;
                }

                var luminance = new byte[sampleWidth * sampleHeight];
                int index = 0;
                double sum = 0;
                double sumSquares = 0;
                int highlights = 0;
                int shadows = 0;

                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y += step)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < accessor.Width; x += step)
                        {
                            var pixel = row[x];
                            int valueInt = ((54 * pixel.R) + (183 * pixel.G) + (19 * pixel.B)) >> 8;
                            byte value = (byte)valueInt;
                            luminance[index++] = value;

                            sum += value;
                            sumSquares += value * value;
                            if (value <= 5)
                                shadows++;
                            if (value >= 250)
                                highlights++;
                        }
                    }
                });

                return TryBuildResult(filePath, luminance, sampleWidth, sampleHeight, index, sum, sumSquares, highlights, shadows, out result);
            }
            catch (Exception ex)
            {
                result.Reason = ex.Message;
                result.Verdict = QualityVerdict.Error;
                return false;
            }
        }

        private static bool TryBuildResult(string filePath, byte[] luminance, int sampleWidth, int sampleHeight, int index, double sum, double sumSquares, int highlights, int shadows, out QualityAssessmentResult result)
        {
            result = new QualityAssessmentResult { SourcePath = filePath, Verdict = QualityVerdict.Error, Reason = "analysis failed" };
            if (index == 0)
            {
                result.Reason = "no sampled pixels";
                return false;
            }

            double count = index;
            double mean = sum / count;
            double variance = Math.Max(0.0, (sumSquares / count) - (mean * mean));
            double stdDev = Math.Sqrt(variance);
            double contrast = stdDev / 255.0;

            double laplacianEnergy = 0;
            int laplacianCount = 0;
            if (sampleWidth > 2 && sampleHeight > 2)
            {
                for (int y = 1; y < sampleHeight - 1; y++)
                {
                    int rowOffset = y * sampleWidth;
                    int upOffset = rowOffset - sampleWidth;
                    int downOffset = rowOffset + sampleWidth;

                    for (int x = 1; x < sampleWidth - 1; x++)
                    {
                        int centerIndex = rowOffset + x;
                        int center = luminance[centerIndex];
                        int left = luminance[centerIndex - 1];
                        int right = luminance[centerIndex + 1];
                        int up = luminance[upOffset + x];
                        int down = luminance[downOffset + x];

                        int laplacian = (4 * center) - left - right - up - down;
                        laplacianEnergy += laplacian * laplacian;
                        laplacianCount++;
                    }
                }
            }

            double sharpness = laplacianCount > 0 ? Math.Sqrt(laplacianEnergy / laplacianCount) : 0.0;
            double clippedHighlightsRatio = highlights / count;
            double clippedShadowsRatio = shadows / count;

            var reasons = new List<string>();
            bool reject = clippedHighlightsRatio > 0.08 || clippedShadowsRatio > 0.08 || contrast < 0.08 || sharpness < 16.0;
            bool review = clippedHighlightsRatio > 0.03 || clippedShadowsRatio > 0.03 || contrast < 0.12 || sharpness < 40.0;

            if (clippedHighlightsRatio > 0.08)
                reasons.Add("clipped highlights > 8%");
            else if (clippedHighlightsRatio > 0.03)
                reasons.Add("clipped highlights > 3%");

            if (clippedShadowsRatio > 0.08)
                reasons.Add("clipped shadows > 8%");
            else if (clippedShadowsRatio > 0.03)
                reasons.Add("clipped shadows > 3%");

            if (contrast < 0.08)
                reasons.Add("contrast < 0.08");
            else if (contrast < 0.12)
                reasons.Add("contrast < 0.12");

            if (sharpness < 16.0)
                reasons.Add("sharpness < 16");
            else if (sharpness < 40.0)
                reasons.Add("sharpness < 40");

            result = new QualityAssessmentResult
            {
                SourcePath = filePath,
                ClippedHighlightsRatio = clippedHighlightsRatio,
                ClippedShadowsRatio = clippedShadowsRatio,
                Contrast = contrast,
                Sharpness = sharpness,
                Verdict = reject ? QualityVerdict.Reject : (review ? QualityVerdict.Review : QualityVerdict.Accept),
                Reason = reasons.Count > 0 ? string.Join(", ", reasons) : "none"
            };

            return true;
        }
    }
}