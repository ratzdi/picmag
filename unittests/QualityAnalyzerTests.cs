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