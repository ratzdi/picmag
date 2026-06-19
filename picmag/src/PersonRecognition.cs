// MIT License
//
// Copyright (c) 2025 Dimitri Ratz
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;
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
    // Configure model paths via PICMAG_FACE_DETECTION_MODEL and PICMAG_FACE_EMBEDDING_MODEL.
    public static class FaceAnalyzer
    {
        private const int EmbeddingInputSize = 112;
        private const int DetectionInputSize = 640;
        private const float DefaultDetectionThreshold = 0.30f;
        private const float DefaultNmsIoUThreshold = 0.45f;
        private const int DefaultMaxFaces = 32;

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

                var faceRegions = new List<DetectedFaceRegion>();
                var configuredDetectionModel = Environment.GetEnvironmentVariable("PICMAG_FACE_DETECTION_MODEL");
                if (LooksLikeEmbeddingModelPath(configuredDetectionModel))
                {
                    reason = "configured detection model looks like an embedding model (e.g. ArcFace). Set PICMAG_FACE_DETECTION_MODEL to a face detector model";
                    return false;
                }

                if (OnnxDetectionRuntime.TryGetInstance(out var detectionRuntime))
                {
                    if (!TryDetectFacesWithOnnx(absoluteFilePath, width, height, detectionRuntime, out faceRegions, out var detectionReason))
                    {
                        reason = string.IsNullOrWhiteSpace(detectionReason)
                            ? "onnx detection failed: no faces detected"
                            : "onnx detection failed: " + detectionReason;
                        return false;
                    }
                }
                else
                {
                    reason = "no detection model available";
                    return false;
                }

                if (faceRegions.Count == 0)
                {
                    reason = "detection completed but no faces found";
                    return false;
                }

                var embeddingModel = "mock-face-embedding-v1";
                var embeddingRuntimeAvailable = OnnxEmbeddingRuntime.TryGetInstance(out var embeddingRuntime);

                for (int i = 0; i < faceRegions.Count; i++)
                {
                    var region = faceRegions[i];
                    var embeddingBytes = CreateFallbackEmbedding(hash, i);

                    if (embeddingRuntimeAvailable)
                    {
                        if (TryCreateOnnxEmbedding(absoluteFilePath, region.Rect, embeddingRuntime, out var onnxEmbedding, out var onnxReason))
                        {
                            embeddingBytes = onnxEmbedding;
                            embeddingModel = embeddingRuntime.ModelName;
                        }
                        else if (string.IsNullOrWhiteSpace(reason))
                        {
                            reason = "onnx embedding fallback to mock embedding: " + onnxReason;
                        }
                    }

                    faces.Add(new FaceDetectionResult
                    {
                        FaceIndex = i,
                        BBoxX = region.Rect.X,
                        BBoxY = region.Rect.Y,
                        BBoxWidth = region.Rect.Width,
                        BBoxHeight = region.Rect.Height,
                        DetectionConfidence = region.Confidence,
                        Embedding = embeddingBytes,
                        EmbeddingModel = embeddingModel
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static bool LooksLikeEmbeddingModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return false;

            var lower = Path.GetFileName(modelPath).ToLowerInvariant();
            return lower.Contains("arcface")
                || lower.Contains("faceresnet")
                || lower.Contains("resnet100")
                || lower.Contains("embedding");
        }

        private static byte[] CreateFallbackEmbedding(byte[] hash, int faceIndex)
        {
            if (faceIndex <= 0)
                return hash;

            var buffer = new byte[hash.Length + sizeof(int)];
            Buffer.BlockCopy(hash, 0, buffer, 0, hash.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(faceIndex), 0, buffer, hash.Length, sizeof(int));
            return SHA256.HashData(buffer);
        }

        private static bool TryDetectFacesWithOnnx(
            string absoluteFilePath,
            int originalWidth,
            int originalHeight,
            OnnxDetectionRuntime runtime,
            out List<DetectedFaceRegion> regions,
            out string reason)
        {
            regions = new List<DetectedFaceRegion>();
            reason = string.Empty;

            try
            {
                using var image = Image.Load<Rgb24>(absoluteFilePath);
                using var resized = image.Clone(ctx => ctx.Resize(runtime.InputWidth, runtime.InputHeight));
                var inputTensor = new DenseTensor<float>(new[] { 1, 3, runtime.InputHeight, runtime.InputWidth });
                var useUltraFaceNormalization = runtime.ModelName.Contains("rfb", StringComparison.OrdinalIgnoreCase)
                    || runtime.ModelName.Contains("ultraface", StringComparison.OrdinalIgnoreCase);

                for (int y = 0; y < runtime.InputHeight; y++)
                {
                    for (int x = 0; x < runtime.InputWidth; x++)
                    {
                        var px = resized[x, y];
                        if (useUltraFaceNormalization)
                        {
                            inputTensor[0, 0, y, x] = (px.R - 127f) / 128f;
                            inputTensor[0, 1, y, x] = (px.G - 127f) / 128f;
                            inputTensor[0, 2, y, x] = (px.B - 127f) / 128f;
                        }
                        else
                        {
                            inputTensor[0, 0, y, x] = px.R / 255f;
                            inputTensor[0, 1, y, x] = px.G / 255f;
                            inputTensor[0, 2, y, x] = px.B / 255f;
                        }
                    }
                }

                var input = NamedOnnxValue.CreateFromTensor(runtime.InputName, inputTensor);
                using var rawOutputs = runtime.Session.Run(new[] { input });

                var candidates = new List<DetectedFaceRegion>();
                var debugOutputs = new List<string>();
                var scoresTensor = (DenseTensor<float>)null;
                var boxesTensor = (DenseTensor<float>)null;

                foreach (var output in rawOutputs)
                {
                    var tensor = output.AsTensor<float>();
                    var dims = string.Join(",", tensor.Dimensions.ToArray());
                    debugOutputs.Add($"{output.Name}: [{dims}]");

                    if (output.Name.Contains("score", StringComparison.OrdinalIgnoreCase))
                        scoresTensor = tensor as DenseTensor<float>;
                    else if (output.Name.Contains("box", StringComparison.OrdinalIgnoreCase))
                        boxesTensor = tensor as DenseTensor<float>;
                    else if (scoresTensor == null)
                        scoresTensor = tensor as DenseTensor<float>;
                    else if (boxesTensor == null)
                        boxesTensor = tensor as DenseTensor<float>;
                }

                if (scoresTensor != null && boxesTensor != null)
                {
                    ParseDetectionPair(scoresTensor, boxesTensor, runtime, originalWidth, originalHeight, candidates);
                }
                else
                {
                    foreach (var output in rawOutputs)
                    {
                        var tensor = output.AsTensor<float>();
                        ParseDetectionTensor(tensor, runtime, originalWidth, originalHeight, candidates);
                    }
                }

                if (candidates.Count == 0)
                {
                    reason = $"no faces above threshold (outputs: {string.Join(" | ", debugOutputs)})";
                    return false;
                }

                regions = ApplyNms(candidates, runtime.NmsIoUThreshold, runtime.MaxFaces);
                if (regions.Count == 0)
                {
                    reason = "all candidates removed by NMS";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static void ParseDetectionPair(
            Tensor<float> scoresTensor,
            Tensor<float> boxesTensor,
            OnnxDetectionRuntime runtime,
            int originalWidth,
            int originalHeight,
            List<DetectedFaceRegion> candidates)
        {
            // UltraFace/RFB-320 outputs:
            // scores: [1, N, 2] -> [background, face]
            // boxes:  [1, N, 4] -> encoded SSD offsets [dx, dy, dw, dh]
            var scoresDims = scoresTensor.Dimensions.ToArray();
            var boxesDims = boxesTensor.Dimensions.ToArray();

            if (scoresDims.Length < 2 || boxesDims.Length < 2)
                return;

            var numAnchors = scoresDims.Length >= 2 ? scoresDims[scoresDims.Length - 2] : 0;
            if (numAnchors <= 0 || boxesDims[boxesDims.Length - 2] != numAnchors)
                return;

            var scoresData = scoresTensor.ToArray();
            var boxesData = boxesTensor.ToArray();

            var strideScore = scoresDims.Length >= 3 ? scoresDims[scoresDims.Length - 1] : 1;
            var strideBox = boxesDims.Length >= 3 ? boxesDims[boxesDims.Length - 1] : 1;
            var priors = GenerateUltraFacePriors(runtime.InputWidth, runtime.InputHeight);
            var useUltraFaceDecode = strideBox >= 4 && priors.Count == numAnchors;

            const float centerVariance = 0.1f;
            const float sizeVariance = 0.2f;

            for (int i = 0; i < numAnchors; i++)
            {
                float faceScore = 0f;

                if (strideScore >= 2)
                {
                    var nonFaceIdx = i * strideScore;
                    var faceIdx = i * strideScore + 1;
                    if (faceIdx < scoresData.Length && nonFaceIdx < scoresData.Length)
                    {
                        var bg = scoresData[nonFaceIdx];
                        var face = scoresData[faceIdx];

                        // Some exports output probabilities, others output logits.
                        // Keep probabilities as-is when they are already normalized.
                        var looksLikeProbabilities =
                            bg >= 0f && bg <= 1f &&
                            face >= 0f && face <= 1f &&
                            Math.Abs((bg + face) - 1f) < 0.05f;

                        if (looksLikeProbabilities)
                        {
                            faceScore = face;
                        }
                        else
                        {
                            var maxLogit = Math.Max(bg, face);
                            var expBg = Math.Exp(bg - maxLogit);
                            var expFace = Math.Exp(face - maxLogit);
                            var denom = expBg + expFace;
                            if (denom > double.Epsilon)
                                faceScore = (float)(expFace / denom);
                        }
                    }
                }
                else if (strideScore == 1)
                {
                    if (i < scoresData.Length)
                        faceScore = scoresData[i];
                }

                if (faceScore < runtime.DetectionThreshold)
                    continue;

                if (strideBox < 4)
                    continue;

                var x_idx = i * strideBox;
                var y_idx = i * strideBox + 1;
                var w_idx = i * strideBox + 2;
                var h_idx = i * strideBox + 3;

                if (x_idx >= boxesData.Length || y_idx >= boxesData.Length
                    || w_idx >= boxesData.Length || h_idx >= boxesData.Length)
                    continue;

                var x = boxesData[x_idx];
                var y = boxesData[y_idx];
                var w = boxesData[w_idx];
                var h = boxesData[h_idx];

                Rectangle rect;
                if (useUltraFaceDecode)
                {
                    var prior = priors[i];
                    var centerX = (x * centerVariance * prior.Width) + prior.CenterX;
                    var centerY = (y * centerVariance * prior.Height) + prior.CenterY;
                    var boxWidth = MathF.Exp(w * sizeVariance) * prior.Width;
                    var boxHeight = MathF.Exp(h * sizeVariance) * prior.Height;

                    var xMin = centerX - (boxWidth / 2f);
                    var yMin = centerY - (boxHeight / 2f);
                    var xMax = centerX + (boxWidth / 2f);
                    var yMax = centerY + (boxHeight / 2f);

                    if (!TryConvertNormalizedCornersToRect(xMin, yMin, xMax, yMax, originalWidth, originalHeight, out rect))
                        continue;
                }
                else if (!TryConvertDetectionToRect(x, y, w, h, runtime, originalWidth, originalHeight, out rect))
                {
                    continue;
                }

                candidates.Add(new DetectedFaceRegion
                {
                    Rect = rect,
                    Confidence = faceScore
                });
            }
        }

        private static List<PriorBox> GenerateUltraFacePriors(int inputWidth, int inputHeight)
        {
            var priors = new List<PriorBox>(4420);

            int[] strides = { 8, 16, 32, 64 };
            int[][] minBoxes =
            {
                new[] { 10, 16, 24 },
                new[] { 32, 48 },
                new[] { 64, 96 },
                new[] { 128, 192, 256 }
            };

            for (int level = 0; level < strides.Length; level++)
            {
                var stride = strides[level];
                var featureMapWidth = (int)Math.Ceiling(inputWidth / (float)stride);
                var featureMapHeight = (int)Math.Ceiling(inputHeight / (float)stride);

                for (int y = 0; y < featureMapHeight; y++)
                {
                    for (int x = 0; x < featureMapWidth; x++)
                    {
                        foreach (var minBox in minBoxes[level])
                        {
                            var centerX = (x + 0.5f) * stride / inputWidth;
                            var centerY = (y + 0.5f) * stride / inputHeight;
                            var width = minBox / (float)inputWidth;
                            var height = minBox / (float)inputHeight;

                            priors.Add(new PriorBox
                            {
                                CenterX = centerX,
                                CenterY = centerY,
                                Width = width,
                                Height = height
                            });
                        }
                    }
                }
            }

            return priors;
        }

        private static bool TryConvertNormalizedCornersToRect(
            float xMin,
            float yMin,
            float xMax,
            float yMax,
            int originalWidth,
            int originalHeight,
            out Rectangle rect)
        {
            rect = Rectangle.Empty;

            if (xMax <= xMin || yMax <= yMin)
                return false;

            var left = xMin * originalWidth;
            var top = yMin * originalHeight;
            var right = xMax * originalWidth;
            var bottom = yMax * originalHeight;

            var x0 = Math.Max(0, (int)Math.Round(left, MidpointRounding.AwayFromZero));
            var y0 = Math.Max(0, (int)Math.Round(top, MidpointRounding.AwayFromZero));
            var x1 = Math.Min(originalWidth, (int)Math.Round(right, MidpointRounding.AwayFromZero));
            var y1 = Math.Min(originalHeight, (int)Math.Round(bottom, MidpointRounding.AwayFromZero));

            var width = x1 - x0;
            var height = y1 - y0;
            if (width < 4 || height < 4)
                return false;

            rect = new Rectangle(x0, y0, width, height);
            return true;
        }

        private static void ParseDetectionTensor(
            Tensor<float> tensor,
            OnnxDetectionRuntime runtime,
            int originalWidth,
            int originalHeight,
            List<DetectedFaceRegion> candidates)
        {
            var dims = tensor.Dimensions.ToArray();
            if (dims.Length < 2)
                return;

            var rows = 0;
            var cols = 0;

            if (dims.Length == 2)
            {
                rows = dims[0];
                cols = dims[1];
            }
            else if (dims.Length == 3)
            {
                rows = dims[0] == 1 ? dims[1] : dims[0] * dims[1];
                cols = dims[dims.Length - 1];
            }
            else
            {
                cols = dims[dims.Length - 1];
                var total = 1;
                for (int i = 0; i < dims.Length - 1; i++)
                    total *= dims[i];
                rows = total;
            }

            if (rows <= 0 || cols < 5)
                return;

            var values = tensor.ToArray();
            for (int row = 0; row < rows; row++)
            {
                var offset = row * cols;
                if (offset + 4 >= values.Length)
                    break;

                var x = values[offset];
                var y = values[offset + 1];
                var w = values[offset + 2];
                var h = values[offset + 3];
                var confidence = values[offset + 4];

                if (cols > 5)
                {
                    var bestClass = 0f;
                    for (int c = 5; c < cols; c++)
                    {
                        if (offset + c >= values.Length)
                            break;

                        if (values[offset + c] > bestClass)
                            bestClass = values[offset + c];
                    }

                    if (bestClass > 0f)
                        confidence *= bestClass;
                }

                if (confidence < runtime.DetectionThreshold)
                    continue;

                if (!TryConvertDetectionToRect(x, y, w, h, runtime, originalWidth, originalHeight, out var rect))
                    continue;

                candidates.Add(new DetectedFaceRegion
                {
                    Rect = rect,
                    Confidence = confidence
                });
            }
        }

        private static bool TryConvertDetectionToRect(
            float x,
            float y,
            float w,
            float h,
            OnnxDetectionRuntime runtime,
            int originalWidth,
            int originalHeight,
            out Rectangle rect)
        {
            rect = Rectangle.Empty;

            if (w <= 0f || h <= 0f)
                return false;

            float left;
            float top;
            float right;
            float bottom;

            var likelyNormalized = x >= 0f && y >= 0f && w <= 1.5f && h <= 1.5f;
            if (likelyNormalized)
            {
                left = x * originalWidth;
                top = y * originalHeight;
                right = left + (w * originalWidth);
                bottom = top + (h * originalHeight);
            }
            else
            {
                var likelyCenterFormat = x > w / 2f && y > h / 2f;
                if (likelyCenterFormat)
                {
                    left = (x - (w / 2f)) * (originalWidth / (float)runtime.InputWidth);
                    top = (y - (h / 2f)) * (originalHeight / (float)runtime.InputHeight);
                }
                else
                {
                    left = x * (originalWidth / (float)runtime.InputWidth);
                    top = y * (originalHeight / (float)runtime.InputHeight);
                }

                right = left + (w * (originalWidth / (float)runtime.InputWidth));
                bottom = top + (h * (originalHeight / (float)runtime.InputHeight));
            }

            var x0 = Math.Max(0, (int)Math.Round(left, MidpointRounding.AwayFromZero));
            var y0 = Math.Max(0, (int)Math.Round(top, MidpointRounding.AwayFromZero));
            var x1 = Math.Min(originalWidth, (int)Math.Round(right, MidpointRounding.AwayFromZero));
            var y1 = Math.Min(originalHeight, (int)Math.Round(bottom, MidpointRounding.AwayFromZero));

            var width = x1 - x0;
            var height = y1 - y0;
            if (width < 4 || height < 4)
                return false;

            rect = new Rectangle(x0, y0, width, height);
            return true;
        }

        private static List<DetectedFaceRegion> ApplyNms(List<DetectedFaceRegion> candidates, float iouThreshold, int maxFaces)
        {
            var ordered = candidates.OrderByDescending(x => x.Confidence).ToList();
            var selected = new List<DetectedFaceRegion>();

            foreach (var candidate in ordered)
            {
                var keep = true;
                foreach (var existing in selected)
                {
                    if (ComputeIoU(candidate.Rect, existing.Rect) > iouThreshold)
                    {
                        keep = false;
                        break;
                    }
                }

                if (!keep)
                    continue;

                selected.Add(candidate);
                if (selected.Count >= maxFaces)
                    break;
            }

            return selected;
        }

        private static float ComputeIoU(Rectangle a, Rectangle b)
        {
            var intersectionLeft = Math.Max(a.Left, b.Left);
            var intersectionTop = Math.Max(a.Top, b.Top);
            var intersectionRight = Math.Min(a.Right, b.Right);
            var intersectionBottom = Math.Min(a.Bottom, b.Bottom);

            var intersectionWidth = Math.Max(0, intersectionRight - intersectionLeft);
            var intersectionHeight = Math.Max(0, intersectionBottom - intersectionTop);
            var intersectionArea = intersectionWidth * intersectionHeight;
            if (intersectionArea <= 0)
                return 0f;

            var unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
            if (unionArea <= 0)
                return 0f;

            return intersectionArea / (float)unionArea;
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

    internal sealed class DetectedFaceRegion
    {
        public Rectangle Rect { get; set; }
        public float Confidence { get; set; }
    }

    internal sealed class PriorBox
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
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

    internal sealed class OnnxDetectionRuntime
    {
        private const string PackagedDetectionModelFilename = "version-RFB-320.onnx";
        private const int DefaultDetectionInputSize = 640;
        private const float DefaultDetectionThreshold = 0.30f;
        private const float DefaultNmsIoUThreshold = 0.45f;
        private const int DefaultMaxFaces = 32;

        public InferenceSession Session { get; }
        public string InputName { get; }
        public string ModelName { get; }
        public int InputWidth { get; }
        public int InputHeight { get; }
        public float DetectionThreshold { get; }
        public float NmsIoUThreshold { get; }
        public int MaxFaces { get; }

        private OnnxDetectionRuntime(
            InferenceSession session,
            string inputName,
            string modelName,
            int inputWidth,
            int inputHeight,
            float detectionThreshold,
            float nmsIoUThreshold,
            int maxFaces)
        {
            Session = session;
            InputName = inputName;
            ModelName = modelName;
            InputWidth = inputWidth;
            InputHeight = inputHeight;
            DetectionThreshold = detectionThreshold;
            NmsIoUThreshold = nmsIoUThreshold;
            MaxFaces = maxFaces;
        }

        private static readonly object sync = new object();
        private static OnnxDetectionRuntime instance;
        private static bool initialized;

        public static string GetDetectionModelSelectionSummary()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("PICMAG_FACE_DETECTION_MODEL");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                var configured = fromEnvironment.Trim();
                if (!Path.IsPathRooted(configured))
                    configured = Path.GetFullPath(configured, Environment.CurrentDirectory);

                if (File.Exists(configured))
                    return $"env override: {configured}";

                return $"env override configured but file missing: {configured}";
            }

            var bundled = Path.Combine(AppContext.BaseDirectory, PackagedDetectionModelFilename);
            if (File.Exists(bundled))
                return $"bundled default: {bundled}";

            return $"no detection model available (expected bundled model at {bundled})";
        }

        public static string GetDetectionParametersSummary()
        {
            var threshold = ReadFloatEnv("PICMAG_FACE_DETECTION_THRESHOLD", DefaultDetectionThreshold, 0.01f, 0.99f);
            var iou = ReadFloatEnv("PICMAG_FACE_DETECTION_NMS_IOU", DefaultNmsIoUThreshold, 0.05f, 0.95f);
            var maxFaces = ReadIntEnv("PICMAG_FACE_DETECTION_MAX_FACES", DefaultMaxFaces, 1, 256);

            return $"threshold={threshold:F2}, nms_iou={iou:F2}, max_faces={maxFaces}";
        }

        public static bool TryGetInstance(out OnnxDetectionRuntime runtime)
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

        private static OnnxDetectionRuntime Create()
        {
            var modelPath = ResolveDetectionModelPath();
            if (string.IsNullOrWhiteSpace(modelPath))
                return null;

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
                NodeMetadata inputMeta = null;
                foreach (var item in session.InputMetadata)
                {
                    inputName = item.Key;
                    inputMeta = item.Value;
                    break;
                }

                if (string.IsNullOrWhiteSpace(inputName) || inputMeta == null)
                {
                    session.Dispose();
                    return null;
                }

                var inputWidth = DefaultDetectionInputSize;
                var inputHeight = DefaultDetectionInputSize;
                if (inputMeta.Dimensions != null && inputMeta.Dimensions.Length >= 4)
                {
                    var h = inputMeta.Dimensions[2];
                    var w = inputMeta.Dimensions[3];
                    if (h > 0)
                        inputHeight = h;
                    if (w > 0)
                        inputWidth = w;
                }

                var modelName = "onnx-face-detection-" + Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();
                var threshold = ReadFloatEnv("PICMAG_FACE_DETECTION_THRESHOLD", DefaultDetectionThreshold, 0.01f, 0.99f);
                var iou = ReadFloatEnv("PICMAG_FACE_DETECTION_NMS_IOU", DefaultNmsIoUThreshold, 0.05f, 0.95f);
                var maxFaces = ReadIntEnv("PICMAG_FACE_DETECTION_MAX_FACES", DefaultMaxFaces, 1, 256);

                return new OnnxDetectionRuntime(
                    session,
                    inputName,
                    modelName,
                    inputWidth,
                    inputHeight,
                    threshold,
                    iou,
                    maxFaces);
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveDetectionModelPath()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("PICMAG_FACE_DETECTION_MODEL");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                var configured = fromEnvironment.Trim();
                if (!Path.IsPathRooted(configured))
                    configured = Path.GetFullPath(configured, Environment.CurrentDirectory);

                return configured;
            }

            return Path.Combine(AppContext.BaseDirectory, PackagedDetectionModelFilename);
        }

        private static float ReadFloatEnv(string name, float fallback, float min, float max)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            if (!float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return fallback;

            if (parsed < min || parsed > max)
                return fallback;

            return parsed;
        }

        private static int ReadIntEnv(string name, int fallback, int min, int max)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return fallback;

            if (parsed < min || parsed > max)
                return fallback;

            return parsed;
        }
    }
}
