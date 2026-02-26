
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Mono.Data.Sqlite;
using System.Data;
using System.Text.Json;

namespace picmag
{
    public class Database
    {
        private CancellationTokenSource cancellationTokenSource;
        private ConcurrentQueue<FileInfo> m_imageQueue = new ConcurrentQueue<FileInfo>();
        private SqliteConnection sqliteConnection;
        private string importDestinationPath;
        private DateTime session_timestamp;
        private ILog log;
        private const String tag = "Data";
        private List<string> extensions;
        private MD5Cache md5Cache;
        private bool deleteSourceAfterImport;
        public uint InsertedImageCount { get; private set; }
        public int AlreadyImportedFileCounter { get; private set; }
        public uint DeletedSourceFileCount { get; private set; }
        public uint DeleteSourceFailedCount { get; private set; }
        public ImagesTable Images { get; private set; }
        public Database(string importDestinationPath, string databaseFilepath, CancellationTokenSource cts, ILog log, List<string> extensions, MD5Cache cache, bool deleteSourceAfterImport = false)
        {
            cancellationTokenSource = cts;
            sqliteConnection = new SqliteConnection(databaseFilepath);
            sqliteConnection.Open();
            Images = new ImagesTable(sqliteConnection, log);
            this.importDestinationPath = importDestinationPath;
            session_timestamp = DateTime.Now;
            this.log = log;
            this.extensions = extensions;
            md5Cache = cache;
            this.deleteSourceAfterImport = deleteSourceAfterImport;
        }
        ~Database()
        {
            if (sqliteConnection != null && sqliteConnection.State != ConnectionState.Closed)
                sqliteConnection.Close();
        }
        public void OnAddFile(object obj, FileFoundEventArgs args)
        {
            m_imageQueue.Enqueue(args.FileInfo);
        }
        public int GetImageQueueSize()
        {
            return m_imageQueue.Count;
        }
        public List<string> GetDuplicates()
        {
            // return duplicateList;
            return null;
        }
        public void StartReceiving()
        {
            FileInfo item;
            var utils = new Utils();
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                if (m_imageQueue.TryDequeue(out item))
                {
                    try
                    {
                        OnJpegExtension(item, utils);
                        OnMp4Extension(item, utils);
                    }
                    catch (Exception ex)
                    {
                        log.PrintError(tag, ex.Message);
                        log.PrintError(tag, ex.StackTrace);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        private void CopyAndInsertFile(FileInfo item, Utils utils, string md5, string targetPath, DateTime creationTime)
        {
            bool canInsert;
            if (!Images.ImageExists(targetPath, md5))
            {
                canInsert = true;
            }
            else
            {
                // duplicateList.Add(item.FullName);
                log.PrintDebug(tag, "Already in DB: " + item.FullName);
                canInsert = false;
            }

            bool canCopy;
            if (System.IO.File.Exists(Path.Combine(this.importDestinationPath, targetPath)) == false)
            {
                canCopy = true;
            }
            else
            {
                canCopy = false;
                log.PrintDebug(tag, "File " + item.Name + " already exists in " + targetPath);
                AlreadyImportedFileCounter++;
            }

            // transaction block
            bool fileInserted = false;
            bool fileCopied = false; ;

            if (canCopy && canInsert)
            {
                try
                {
                    // copy file
                    var fullDestinationFilePath = Path.Combine(this.importDestinationPath, targetPath);
                    utils.CopyFile(item.FullName, fullDestinationFilePath);
                    log.PrintDebug(tag, "Copied: {0} to {1}", item.Name, targetPath);
                    fileCopied = true;
                }
                catch (Exception ex)
                {
                    // TODO: undo insert
                    fileCopied = false;
                    log.PrintError(tag, ex.Message);
                    log.PrintError(tag, ex.StackTrace);
                }
                if (fileCopied)
                {
                    try
                    {
                        // insert db
                        Images.Insert(targetPath, creationTime, md5);
                        fileInserted = true;
                    }
                    catch (Exception ex)
                    {
                        fileInserted = false;
                        utils.RemoveFile(Path.Combine(this.importDestinationPath, targetPath));
                        log.PrintError(tag, ex.StackTrace);
                    }
                }

                if (fileInserted && fileCopied)
                {
                    md5Cache.TryAdd(item.FullName, md5);
                    log.PrintInfo(tag, "Imported: {0} to {1}", item.Name, targetPath);
                    InsertedImageCount++;

                    if (deleteSourceAfterImport)
                    {
                        try
                        {
                            utils.RemoveFile(item.FullName);
                            log.PrintInfo(tag, "Deleted source file: {0}", item.FullName);
                            DeletedSourceFileCount++;
                        }
                        catch (Exception ex)
                        {
                            DeleteSourceFailedCount++;
                            log.PrintError(tag, "Failed to delete source file: {0}", item.FullName);
                            log.PrintError(tag, ex.Message);
                            log.PrintError(tag, ex.StackTrace);
                        }
                    }
                }
            }
        }

        private bool OnJpegExtension(FileInfo item, Utils utils)
        {
            if ((item.Extension.ToLower().Trim('.').Equals("jpeg") || item.Extension.ToLower().Trim('.').Equals("jpg")) && extensions.Contains(item.Extension.ToLower().Trim('.')))
            {
                string md5 = string.Empty;

                if (md5Cache.TryGetValue(item.FullName, out md5))
                {
                    return false;
                }

                DateTime creationTime;
                var fileName = string.Empty;
                ExifLib.JpegInfo jpegInfo = null;

                try
                {
                    jpegInfo = ExifLib.ExifReader.ReadJpeg(item);
                }
                catch (Exception ex)
                {
                    log.PrintError(tag, ex.Message);
                    log.PrintError(tag, ex.StackTrace);
                }

                if (jpegInfo != null && jpegInfo.MD5 != null)
                {
                    md5 = BitConverter.ToString(jpegInfo.MD5);
                }
                else
                {
                    // try to get the md5 from local application cache
                    md5 = BitConverter.ToString(utils.GetMd5(item.FullName));
                }

                string dirPath;
                if (jpegInfo != null)
                {
                    fileName = jpegInfo.FileName;

                    try
                    {
                        dirPath = utils.CreateDirectoryPathFrom(jpegInfo.DateTime);
                    }
                    catch (Exception ex)
                    {
                        log.PrintError(tag, "Timestamp of the image not valid or available: " + jpegInfo.FileName);
                        log.PrintError(tag, "Try to use file creation time instead: " + item.FullName);
                        log.PrintError(tag, ex.Message);
                        log.PrintError(tag, ex.StackTrace);
                        dirPath = utils.CreateDirectoryPathFrom(item.CreationTime);
                    }

                    try
                    {
                        creationTime = utils.ToDateTime(jpegInfo.DateTime);
                    }
                    catch (Exception ex)
                    {
                        creationTime = item.CreationTime;
                        log.PrintError(tag, ex.Message);
                        log.PrintError(tag, ex.StackTrace);
                    }
                }
                else
                {
                    dirPath = utils.CreateDirectoryPathFrom(item.CreationTime);
                    creationTime = item.CreationTime;
                }

                string targetPath = Path.Combine(dirPath, fileName);
                CopyAndInsertFile(item, utils, md5, targetPath, creationTime);

                return true;
            }
            return false;
        }
        private bool OnMp4Extension(FileInfo item, Utils utils)
        {
            if (item.Extension.ToLower().Trim('.').Equals("mp4") && extensions.Contains(item.Extension.ToLower().Trim('.')))
            {
                string md5 = string.Empty;
                if (md5Cache.TryGetValue(item.FullName, out md5))
                {
                    return false;
                }

                DateTime creationTime;
                if (!TryGetMp4CreationTime(item, out creationTime))
                {
                    creationTime = item.CreationTime;
                    log.PrintDebug(tag, "ffprobe metadata not available, use file creation time for {0}", item.FullName);
                }

                md5 = BitConverter.ToString(utils.GetMd5(item.FullName));
                var targetPath = Path.Combine(utils.CreateDirectoryPathFrom(creationTime), item.Name);
                CopyAndInsertFile(item, utils, md5, targetPath, creationTime);
                return true;
            }
            return false;
        }

        private bool TryGetMp4CreationTime(FileInfo item, out DateTime creationTime)
        {
            creationTime = item.CreationTime;
            try
            {
                var process = new Process();
                process.StartInfo.FileName = "ffprobe";
                process.StartInfo.Arguments = "-v quiet -print_format json -show_entries format_tags=creation_time:stream_tags=creation_time \"" + item.FullName + "\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                var output = process.StandardOutput.ReadToEnd();

                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    return false;
                }

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return false;

                using (var document = JsonDocument.Parse(output))
                {
                    if (TryReadCreationTimeFromJson(document.RootElement, out creationTime))
                        return true;
                }
            }
            catch (Exception ex)
            {
                log.PrintDebug(tag, "ffprobe failed for {0}: {1}", item.FullName, ex.Message);
            }

            return false;
        }

        private bool TryReadCreationTimeFromJson(JsonElement root, out DateTime creationTime)
        {
            creationTime = DateTime.MinValue;

            JsonElement format;
            if (root.TryGetProperty("format", out format))
            {
                JsonElement tags;
                if (format.TryGetProperty("tags", out tags))
                {
                    JsonElement value;
                    if (tags.TryGetProperty("creation_time", out value) && TryParseCreationTime(value.GetString(), out creationTime))
                        return true;
                }
            }

            JsonElement streams;
            if (root.TryGetProperty("streams", out streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    JsonElement tags;
                    if (stream.TryGetProperty("tags", out tags))
                    {
                        JsonElement value;
                        if (tags.TryGetProperty("creation_time", out value) && TryParseCreationTime(value.GetString(), out creationTime))
                            return true;
                    }
                }
            }

            return false;
        }

        private bool TryParseCreationTime(string creationTimeRaw, out DateTime creationTime)
        {
            creationTime = DateTime.MinValue;
            DateTimeOffset parsed;
            if (string.IsNullOrWhiteSpace(creationTimeRaw))
                return false;

            if (!DateTimeOffset.TryParse(creationTimeRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return false;

            creationTime = parsed.LocalDateTime;
            return true;
        }
    }
}
