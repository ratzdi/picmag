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

using Mono.Data.Sqlite;
using picmag;

namespace unittests
{
    [TestClass]
    public class PersonRecognitionTablesTests
    {
        private SqliteConnection connection = null!;
        private ImagesTable images = null!;
        private PersonsTable persons = null!;
        private ImageFacesTable imageFaces = null!;
        private PersonProfilesTable personProfiles = null!;
        private PersonPredictionsTable personPredictions = null!;
        private MockLog mockLog = null!;

        [TestInitialize]
        public void Setup()
        {
            connection = new SqliteConnection("URI=file::memory:");
            connection.Open();

            mockLog = new MockLog();
            images = new ImagesTable(connection, mockLog);
            images.Create();
            persons = new PersonsTable(connection);
            imageFaces = new ImageFacesTable(connection);
            personProfiles = new PersonProfilesTable(connection);
            personPredictions = new PersonPredictionsTable(connection);
        }

        [TestCleanup]
        public void Cleanup()
        {
            connection.Dispose();
        }

        [TestMethod]
        public void Persons_AddOrGetId_IsCaseInsensitive()
        {
            var id1 = persons.AddOrGetId("Alice");
            var id2 = persons.AddOrGetId(" alice ");

            Assert.AreEqual(id1, id2);
            Assert.AreEqual(1, persons.GetAll().Count);
        }

        [TestMethod]
        public void ImageFaces_LabelWorkflow_UpdatesUnlabeledAndConfirmedSearch()
        {
            const string imagePath = "gallery/one.jpg";
            images.Insert(imagePath, DateTime.UtcNow, "MD5-A");

            imageFaces.UpsertFace(imagePath, NewFace(0, "model-a", new float[] { 1f, 0f, 0f }));

            var unlabeledBefore = imageFaces.GetUnlabeledFaces(10);
            Assert.AreEqual(1, unlabeledBefore.Count);

            var personId = persons.AddOrGetId("Bob");
            imageFaces.UpsertLabel(unlabeledBefore[0].FaceId, personId, FaceLabelStatus.Confirmed);

            var unlabeledAfter = imageFaces.GetUnlabeledFaces(10);
            Assert.AreEqual(0, unlabeledAfter.Count);

            var confirmedImages = imageFaces.GetConfirmedImagePathsByPerson("bob");
            Assert.AreEqual(1, confirmedImages.Count);
            Assert.AreEqual(imagePath, confirmedImages[0]);
        }

        [TestMethod]
        public void PersonProfiles_UpsertAndClearAll_WorksPerModel()
        {
            var personId = persons.AddOrGetId("Carla");

            personProfiles.UpsertProfile(personId, "model-a", EmbeddingBytes(0.1f, 0.2f, 0.3f), 2);
            personProfiles.UpsertProfile(personId, "model-b", EmbeddingBytes(0.4f, 0.5f, 0.6f), 3);

            var metadata = personProfiles.GetAllMetadata();
            Assert.AreEqual(2, metadata.Count);
            Assert.IsTrue(metadata.Any(m => m.EmbeddingModel == "model-a" && m.SampleCount == 2));
            Assert.IsTrue(metadata.Any(m => m.EmbeddingModel == "model-b" && m.SampleCount == 3));

            var embeddings = personProfiles.GetAllEmbeddings();
            Assert.AreEqual(2, embeddings.Count);
            Assert.IsTrue(embeddings.All(e => e.PersonId == personId));

            personProfiles.ClearAll();
            Assert.AreEqual(0, personProfiles.GetAllMetadata().Count);
        }

        [TestMethod]
        public void PersonPredictions_SuggestedOnlyAndClearSuggested_Works()
        {
            const string imagePath = "gallery/two.jpg";
            images.Insert(imagePath, DateTime.UtcNow, "MD5-B");
            imageFaces.UpsertFace(imagePath, NewFace(0, "model-a", new float[] { 0.9f, 0.1f, 0.2f }));

            var faceId = imageFaces.GetUnlabeledFaces(10).Single().FaceId;
            var personId = persons.AddOrGetId("Dora");

            personPredictions.UpsertPrediction(faceId, personId, 0.87d, PredictionStatus.Suggested);
            var suggested = personPredictions.GetSuggestedPredictions(10);
            Assert.AreEqual(1, suggested.Count);
            Assert.AreEqual("suggested", suggested[0].Status);
            Assert.AreEqual(imagePath, suggested[0].ImagePath);

            personPredictions.UpsertPrediction(faceId, personId, 0.88d, PredictionStatus.Confirmed);
            Assert.AreEqual(0, personPredictions.GetSuggestedPredictions(10).Count);

            personPredictions.UpsertPrediction(faceId, personId, 0.89d, PredictionStatus.Suggested);
            Assert.AreEqual(1, personPredictions.GetSuggestedPredictions(10).Count);

            personPredictions.ClearSuggested();
            Assert.AreEqual(0, personPredictions.GetSuggestedPredictions(10).Count);
        }

        [TestMethod]
        public void ConfirmedLabels_AppearInConfirmedEmbeddings()
        {
            const string imagePath = "gallery/three.jpg";
            images.Insert(imagePath, DateTime.UtcNow, "MD5-C");
            imageFaces.UpsertFace(imagePath, NewFace(0, "model-a", new float[] { 0.1f, 0.9f, 0.2f }));

            var faceId = imageFaces.GetUnlabeledFaces(10).Single().FaceId;
            var personId = persons.AddOrGetId("Eve");

            imageFaces.UpsertLabel(faceId, personId, FaceLabelStatus.Confirmed);

            var confirmedEmbeddings = imageFaces.GetConfirmedEmbeddings();
            Assert.AreEqual(1, confirmedEmbeddings.Count);
            Assert.AreEqual(personId, confirmedEmbeddings[0].PersonId);
            Assert.AreEqual("Eve", confirmedEmbeddings[0].PersonName);
            Assert.AreEqual("model-a", confirmedEmbeddings[0].EmbeddingModel);
            CollectionAssert.AreEqual(EmbeddingBytes(0.1f, 0.9f, 0.2f), confirmedEmbeddings[0].Embedding);
        }

        private static FaceDetectionResult NewFace(int index, string model, float[] embedding)
        {
            return new FaceDetectionResult
            {
                FaceIndex = index,
                BBoxX = 1,
                BBoxY = 2,
                BBoxWidth = 32,
                BBoxHeight = 32,
                DetectionConfidence = 0.95,
                EmbeddingModel = model,
                Embedding = EmbeddingBytes(embedding)
            };
        }

        private static byte[] EmbeddingBytes(params float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}