using Mono.Data.Sqlite;
using picmag;

namespace unittests
{
    [TestClass]
    public class ImagesTableTests
    {
        private SqliteConnection connection = null!;
        private ImagesTable table = null!;
        private MockLog mockLog = null!;

        [TestInitialize]
        public void Setup()
        {
            connection = new SqliteConnection("URI=file::memory:");
            connection.Open();
            mockLog = new MockLog();
            table = new ImagesTable(connection, mockLog);
            table.Create();
        }

        [TestCleanup]
        public void Cleanup()
        {
            connection.Dispose();
        }

        [TestMethod]
        public void ImageExists_WithQuotedValues_ReturnsTrue()
        {
            var path = "2018/11/30/quote's-file.jpg";
            var md5 = "AA-BB-'CC";
            table.Insert(path, DateTime.Now, md5);

            var exists = table.ImageExists(path, md5);

            Assert.IsTrue(exists);
        }

        [TestMethod]
        public void FindDuplicates_ReturnsCountOfRowsInDuplicateGroups()
        {
            table.Insert("a.jpg", DateTime.Now, "DUP-MD5");
            table.Insert("b.jpg", DateTime.Now, "DUP-MD5");
            table.Insert("c.jpg", DateTime.Now, "UNIQUE-MD5");

            var duplicateCount = table.FindDuplicates();

            Assert.AreEqual(2, duplicateCount);
        }

        [TestMethod]
        public void RemoveByPath_RemovesExistingRow()
        {
            var path = "remove/me.jpg";
            table.Insert(path, DateTime.Now, "RM-MD5");

            var removed = table.RemoveByPath(path);

            Assert.AreEqual(1, removed);
            Assert.IsFalse(table.ImageExists(path));
        }

        [TestMethod]
        public void GetByQualityVerdict_ReturnsRowsWithStoredQualityMetadata()
        {
            var quality = new QualityAssessmentResult
            {
                Verdict = QualityVerdict.Reject,
                Reason = "contrast < 0.08",
                Contrast = 0.05,
                Sharpness = 12,
                ClippedHighlightsRatio = 0.01,
                ClippedShadowsRatio = 0.1
            };

            table.Insert("quality/reject.jpg", DateTime.Now, "Q-MD5-1", quality);
            table.Insert("quality/accept.jpg", DateTime.Now, "Q-MD5-2", new QualityAssessmentResult { Verdict = QualityVerdict.Accept, Reason = "none" });

            var rejected = table.GetByQualityVerdict(QualityReviewVerdict.Reject);

            Assert.AreEqual(1, rejected.Count);
            Assert.AreEqual("quality/reject.jpg", rejected[0].Path);
            Assert.AreEqual("reject", rejected[0].QualityVerdict);
            Assert.AreEqual("contrast < 0.08", rejected[0].QualityReason);
        }

        [TestMethod]
        public void GetJpegPathsForQualityScan_OnlyMissing_ReturnsOnlyRowsWithoutVerdict()
        {
            table.Insert("quality/missing.jpg", DateTime.Now, "M-MD5-1");
            table.Insert("quality/present.jpg", DateTime.Now, "M-MD5-2", new QualityAssessmentResult { Verdict = QualityVerdict.Accept, Reason = "none" });
            table.Insert("quality/video.mp4", DateTime.Now, "M-MD5-3");

            var paths = table.GetJpegPathsForQualityScan(true);

            Assert.AreEqual(1, paths.Count);
            Assert.AreEqual("quality/missing.jpg", paths[0]);
        }

        [TestMethod]
        public void UpdateQualityMetadata_UpdatesStoredVerdictAndReason()
        {
            const string path = "quality/update.jpg";
            table.Insert(path, DateTime.Now, "U-MD5-1");

            var assessment = new QualityAssessmentResult
            {
                Verdict = QualityVerdict.Review,
                Reason = "contrast < 0.12",
                Contrast = 0.1,
                Sharpness = 30,
                ClippedHighlightsRatio = 0.02,
                ClippedShadowsRatio = 0.03
            };

            var updated = table.UpdateQualityMetadata(path, assessment);
            var reviewRows = table.GetByQualityVerdict(QualityReviewVerdict.Review);

            Assert.AreEqual(1, updated);
            Assert.AreEqual(1, reviewRows.Count);
            Assert.AreEqual(path, reviewRows[0].Path);
            Assert.AreEqual("contrast < 0.12", reviewRows[0].QualityReason);
        }
    }
}
