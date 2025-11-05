using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace picmag
{
    public class MD5Cache : IMD5Cache
    {
        private readonly string cacheFilepath;
        private readonly ILog log;
        private Dictionary<string, string> cache;
        private StreamWriter fileStream;
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
            fileStream.WriteLine(key + " " + value);
            fileStream.Flush();
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
        public Dictionary<string, string> ReadKeyValueFile(string path)
        {
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

                var array = line.Split(' ');
                if (array.Length != 2)
                    continue;
                var key = array.ElementAt(0);
                var value = array.ElementAt(1);
                if (key.Length == 0 || value.Length == 0)
                    continue;
                dictionary[key] = value;
            }
            return dictionary;
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
    }
}