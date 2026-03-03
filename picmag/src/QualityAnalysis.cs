using System;
using System.Collections.Generic;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        public static bool TryAnalyzeJpeg(string filePath, out QualityAssessmentResult result)
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
                        for (int x = 1; x < sampleWidth - 1; x++)
                        {
                            int centerIndex = y * sampleWidth + x;
                            int center = luminance[centerIndex];
                            int left = luminance[centerIndex - 1];
                            int right = luminance[centerIndex + 1];
                            int up = luminance[centerIndex - sampleWidth];
                            int down = luminance[centerIndex + sampleWidth];

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
            catch (Exception ex)
            {
                result.Reason = ex.Message;
                result.Verdict = QualityVerdict.Error;
                return false;
            }
        }
    }
}