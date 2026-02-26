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
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace picmag
{
    public class MD5Cache : IMD5Cache, IDisposable
    {
        private readonly string cacheFilepath;
        private readonly ILog log;
        private readonly ConcurrentDictionary<string, string> cache;
        private readonly StreamWriter fileStream;
        private readonly object fileStreamLock = new object();
        private bool disposed;
        static readonly string tag = "Cache";
        public MD5Cache(string _cacheFilepath, ILog _log)
        {
            cacheFilepath = _cacheFilepath;
            log = _log;
            cache = ReadKeyValueFile(cacheFilepath);
            fileStream = new StreamWriter(new FileStream(cacheFilepath, FileMode.Append, FileAccess.Write, FileShare.Read));
        }
        void PersistCache(string key, string value)
        {
            string line = Convert.ToBase64String(Encoding.UTF8.GetBytes(key)) + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            try
            {
                lock (fileStreamLock)
                {
                    fileStream.WriteLine(line);
                    fileStream.Flush();
                }
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Error writing cache file: " + ex.Message);
            }
        }
        public bool TryGetValue(string key, out string value)
        {
            if (cache.TryGetValue(key, out value))
            {
                log.PrintDebug(tag, "Hit: " + key);
                return true;
            }
            value = null;
            return false;
        }
        public ConcurrentDictionary<string, string> ReadKeyValueFile(string path)
        {
            var dictionary = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                try
                {
                    File.Create(path).Dispose();
                }
                catch (Exception ex)
                {
                    log.PrintError(tag, "Error creating cache file: " + ex.Message);
                }
                return dictionary;
            }

            foreach (var rawLine in File.ReadLines(path))
            {
                if (rawLine == null)
                    continue;
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                string key;
                string value;

                if (!TryParseLine(line, out key, out value))
                    continue;

                dictionary[key] = value;
            }
            return dictionary;
        }

        private bool TryParseLine(string line, out string key, out string value)
        {
            key = null;
            value = null;

            var array = line.Split('\t');
            if (array.Length == 2)
            {
                try
                {
                    key = Encoding.UTF8.GetString(Convert.FromBase64String(array[0]));
                    value = Encoding.UTF8.GetString(Convert.FromBase64String(array[1]));
                    return key.Length > 0 && value.Length > 0;
                }
                catch
                {
                }
            }

            var separatorIndex = line.IndexOf(' ');
            if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                return false;

            key = line.Substring(0, separatorIndex);
            value = line.Substring(separatorIndex + 1);
            return key.Length > 0 && value.Length > 0;
        }

        public bool TryAdd(string key, string value)
        {
            bool isAdded = cache.TryAdd(key, value);
            if (isAdded)
            {
                PersistCache(key, value);
                log.PrintDebug(tag, "Added to cache: " + key);
            }
            return isAdded;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                lock (fileStreamLock)
                {
                    fileStream.Dispose();
                }
            }
            disposed = true;
        }

        ~MD5Cache()
        {
            Dispose(false);
        }
    }
}