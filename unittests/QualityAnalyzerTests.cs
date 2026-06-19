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
using System.IO;
using picmag;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace unittests
{
    [TestClass]
    public class QualityAnalyzerTests
    {
        [TestMethod]
        public void TryAnalyzeJpeg_WithLowContrastImage_ReturnsRejectOrReview()
        {
            var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                using (var image = new Image<Rgb24>(64, 64))
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            for (int x = 0; x < row.Length; x++)
                            {
                                row[x] = new Rgb24(128, 128, 128);
                            }
                        }
                    });

                    image.SaveAsJpeg(filePath);
                }

                var success = QualityAnalyzer.TryAnalyzeJpeg(filePath, out var result);

                Assert.IsTrue(success);
                Assert.IsTrue(result.Verdict == QualityVerdict.Review || result.Verdict == QualityVerdict.Reject);
                Assert.IsTrue(result.Contrast < 0.12);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }

        [TestMethod]
        public void TryAnalyzeJpeg_WithNonImageFile_ReturnsFalse()
        {
            var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                File.WriteAllText(filePath, "not-an-image");

                var success = QualityAnalyzer.TryAnalyzeJpeg(filePath, out var result);

                Assert.IsFalse(success);
                Assert.AreEqual(QualityVerdict.Error, result.Verdict);
            }
            finally
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
    }
}