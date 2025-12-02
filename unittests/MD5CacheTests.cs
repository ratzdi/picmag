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

using picmag;

namespace unittests
{
    [TestClass]
    public class MD5CacheTests
    {
        private string tempCacheFile;
        private MockLog mockLog;

        [TestInitialize]
        public void Setup()
        {
            tempCacheFile = Path.GetTempFileName();
            mockLog = new MockLog();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(tempCacheFile))
            {
                File.Delete(tempCacheFile);
            }
        }

        [TestMethod]
        public void Constructor_WhenFileDoesNotExist_CreatesNewFile()
        {
            // Arrange
            if (File.Exists(tempCacheFile))
            {
                File.Delete(tempCacheFile);
            }

            // Act
            var cache = new MD5Cache(tempCacheFile, mockLog);

            // Assert
            Assert.IsTrue(File.Exists(tempCacheFile));
        }

        [TestMethod]
        public void TryAdd_NewKey_ReturnsTrue()
        {
            // Arrange
            var cache = new MD5Cache(tempCacheFile, mockLog);
            
            // Act
            bool result = cache.TryAdd("testKey", "testValue");

            // Assert
            Assert.IsTrue(result);
            string value;
            Assert.IsTrue(cache.TryGetValue("testKey", out value));
            Assert.AreEqual("testValue", value);
        }

        [TestMethod]
        public void TryAdd_DuplicateKey_ReturnsFalse()
        {
            // Arrange
            var cache = new MD5Cache(tempCacheFile, mockLog);
            cache.TryAdd("testKey", "testValue1");

            // Act
            bool result = cache.TryAdd("testKey", "testValue2");

            // Assert
            Assert.IsFalse(result);
            string value;
            Assert.IsTrue(cache.TryGetValue("testKey", out value));
            Assert.AreEqual("testValue1", value); // Original value should remain
        }

        [TestMethod]
        public void TryGetValue_ExistingKey_ReturnsTrue()
        {
            // Arrange
            var cache = new MD5Cache(tempCacheFile, mockLog);
            cache.TryAdd("testKey", "testValue");

            // Act
            string value;
            bool result = cache.TryGetValue("testKey", out value);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual("testValue", value);
        }

        [TestMethod]
        public void TryGetValue_NonExistentKey_ReturnsFalse()
        {
            // Arrange
            var cache = new MD5Cache(tempCacheFile, mockLog);

            // Act
            string value;
            bool result = cache.TryGetValue("nonexistentKey", out value);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(value);
        }

        [TestMethod]
        public void ReadKeyValueFile_ValidFile_LoadsExistingEntries()
        {
            // Arrange
            File.WriteAllText(tempCacheFile, "key1 value1\nkey2 value2");
            
            // Act
            var cache = new MD5Cache(tempCacheFile, mockLog);

            // Assert
            string value1, value2;
            Assert.IsTrue(cache.TryGetValue("key1", out value1));
            Assert.IsTrue(cache.TryGetValue("key2", out value2));
            Assert.AreEqual("value1", value1);
            Assert.AreEqual("value2", value2);
        }
    }

    // Mock implementation of ILog for testing
    public class MockLog : ILog
    {
        public void PrintDebug(string component, string message) { }

        public void PrintDebug(string tag, string format, params object[] msg)
        {
        }

        public void PrintError(string component, string message) { }

        public void PrintError(string tag, string format, params object[] msg)
        {
        }

        public void PrintInfo(string component, string message) { }

        public void PrintInfo(string tag, string format, params object[] msg)
        {
        }
    }
}