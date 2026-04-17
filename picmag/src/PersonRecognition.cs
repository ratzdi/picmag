using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace picmag
{
    public enum FaceLabelStatus
    {
        Confirmed,
        Rejected
    }

    public class FaceDetectionResult
    {
        public int FaceIndex { get; set; }
        public double BBoxX { get; set; }
        public double BBoxY { get; set; }
        public double BBoxWidth { get; set; }
        public double BBoxHeight { get; set; }
        public double DetectionConfidence { get; set; }
        public byte[] Embedding { get; set; } = Array.Empty<byte>();
        public string EmbeddingModel { get; set; } = "mock-face-embedding-v1";
    }

    // MVP analyzer with optional ONNX embedding backend.
    // Configure model path via PICMAG_FACE_EMBEDDING_MODEL.
    public static class FaceAnalyzer
    {
        private const int EmbeddingInputSize = 112;

        public static bool TryAnalyzeImage(string absoluteFilePath, out List<FaceDetectionResult> faces, out string reason)
        {
            faces = new List<FaceDetectionResult>();
            reason = string.Empty;

            try
            {
                var fileBytes = File.ReadAllBytes(absoluteFilePath);
                if (fileBytes.Length == 0)
                {
                    reason = "empty file";
                    return false;
                }

                var imageInfo = Image.Identify(absoluteFilePath);
                if (imageInfo == null)
                {
                    reason = "could not read image metadata";
                    return false;
                }

                var width = Math.Max(imageInfo.Width, 1);
                var height = Math.Max(imageInfo.Height, 1);
                var hash = SHA256.HashData(fileBytes);
                var embeddingModel = "mock-face-embedding-v1";
                var embeddingBytes = hash;

                var centerRect = CreateCenteredRect(width, height);
                if (OnnxEmbeddingRuntime.TryGetInstance(out var runtime))
                {
                    if (TryCreateOnnxEmbedding(absoluteFilePath, centerRect, runtime, out var onnxEmbedding, out var onnxReason))
                    {
                        embeddingBytes = onnxEmbedding;
                        embeddingModel = runtime.ModelName;
                    }
                    else
                    {
                        reason = "onnx fallback to mock embedding: " + onnxReason;
                    }
                }

                faces.Add(new FaceDetectionResult
                {
                    FaceIndex = 0,
                    BBoxX = centerRect.X,
                    BBoxY = centerRect.Y,
                    BBoxWidth = centerRect.Width,
                    BBoxHeight = centerRect.Height,
                    DetectionConfidence = 0.5,
                    Embedding = embeddingBytes,
                    EmbeddingModel = embeddingModel
                });

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static Rectangle CreateCenteredRect(int width, int height)
        {
            var boxX = (int)Math.Round(width * 0.2, MidpointRounding.AwayFromZero);
            var boxY = (int)Math.Round(height * 0.2, MidpointRounding.AwayFromZero);
            var boxWidth = Math.Max((int)Math.Round(width * 0.6, MidpointRounding.AwayFromZero), 1);
            var boxHeight = Math.Max((int)Math.Round(height * 0.6, MidpointRounding.AwayFromZero), 1);

            if (boxX + boxWidth > width)
                boxWidth = Math.Max(width - boxX, 1);
            if (boxY + boxHeight > height)
                boxHeight = Math.Max(height - boxY, 1);

            return new Rectangle(boxX, boxY, boxWidth, boxHeight);
        }

        private static bool TryCreateOnnxEmbedding(string absoluteFilePath, Rectangle faceRect, OnnxEmbeddingRuntime runtime, out byte[] embeddingBytes, out string reason)
        {
            embeddingBytes = Array.Empty<byte>();
            reason = string.Empty;

            try
            {
                using var image = Image.Load<Rgb24>(absoluteFilePath);
                var boundedRect = Rectangle.Intersect(faceRect, new Rectangle(0, 0, image.Width, image.Height));
                if (boundedRect.Width < 1 || boundedRect.Height < 1)
                {
                    reason = "invalid face rectangle for embedding";
                    return false;
                }

                using var faceImage = image.Clone(ctx => ctx.Crop(boundedRect).Resize(EmbeddingInputSize, EmbeddingInputSize));
                var inputTensor = new DenseTensor<float>(new[] { 1, 3, EmbeddingInputSize, EmbeddingInputSize });

                for (int y = 0; y < EmbeddingInputSize; y++)
                {
                    for (int x = 0; x < EmbeddingInputSize; x++)
                    {
                        var px = faceImage[x, y];
                        inputTensor[0, 0, y, x] = px.R / 255f;
                        inputTensor[0, 1, y, x] = px.G / 255f;
                        inputTensor[0, 2, y, x] = px.B / 255f;
                    }
                }

                var input = NamedOnnxValue.CreateFromTensor(runtime.InputName, inputTensor);
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = runtime.Session.Run(new[] { input });

                float[] output;
                using (var enumerator = results.GetEnumerator())
                {
                    if (!enumerator.MoveNext())
                    {
                        reason = "onnx model returned no outputs";
                        return false;
                    }

                    output = enumerator.Current.AsEnumerable<float>()?.ToArray();
                }

                if (output == null || output.Length == 0)
                {
                    reason = "onnx embedding output empty";
                    return false;
                }

                var normalized = L2Normalize(output);
                embeddingBytes = FloatArrayToBytes(normalized);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static float[] L2Normalize(float[] values)
        {
            double sumSquares = 0;
            for (int i = 0; i < values.Length; i++)
                sumSquares += values[i] * values[i];

            if (sumSquares <= double.Epsilon)
                return values;

            var scale = 1.0 / Math.Sqrt(sumSquares);
            for (int i = 0; i < values.Length; i++)
                values[i] = (float)(values[i] * scale);

            return values;
        }

        private static byte[] FloatArrayToBytes(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                var floatBytes = BitConverter.GetBytes(values[i]);
                Buffer.BlockCopy(floatBytes, 0, bytes, i * sizeof(float), sizeof(float));
            }

            return bytes;
        }
    }

    internal sealed class OnnxEmbeddingRuntime
    {
        public InferenceSession Session { get; }
        public string InputName { get; }
        public string ModelName { get; }

        private OnnxEmbeddingRuntime(InferenceSession session, string inputName, string modelName)
        {
            Session = session;
            InputName = inputName;
            ModelName = modelName;
        }

        private static readonly object sync = new object();
        private static OnnxEmbeddingRuntime instance;
        private static bool initialized;

        public static bool TryGetInstance(out OnnxEmbeddingRuntime runtime)
        {
            runtime = null;

            if (initialized)
            {
                runtime = instance;
                return runtime != null;
            }

            lock (sync)
            {
                if (!initialized)
                {
                    instance = Create();
                    initialized = true;
                }

                runtime = instance;
                return runtime != null;
            }
        }

        private static OnnxEmbeddingRuntime Create()
        {
            var modelPath = Environment.GetEnvironmentVariable("PICMAG_FACE_EMBEDDING_MODEL");
            if (string.IsNullOrWhiteSpace(modelPath))
                return null;

            modelPath = modelPath.Trim();
            if (!Path.IsPathRooted(modelPath))
                modelPath = Path.GetFullPath(modelPath, Environment.CurrentDirectory);

            if (!File.Exists(modelPath))
                return null;

            try
            {
                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                var session = new InferenceSession(modelPath, options);
                string inputName = null;
                foreach (var item in session.InputMetadata)
                {
                    inputName = item.Key;
                    break;
                }

                if (string.IsNullOrWhiteSpace(inputName))
                {
                    session.Dispose();
                    return null;
                }

                var modelName = "onnx-face-embedding-" + Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();
                return new OnnxEmbeddingRuntime(session, inputName, modelName);
            }
            catch
            {
                return null;
            }
        }
    }
}
