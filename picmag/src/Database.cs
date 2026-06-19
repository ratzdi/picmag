
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
    public class Database : IDisposable
    {
        public class SanityCheckReport
        {
            public List<string> MissingDatabaseEntries { get; } = new List<string>();
            public List<string> OrphanDatabaseEntries { get; } = new List<string>();
            public int InsertedDatabaseEntries { get; set; }
            public int RemovedDatabaseEntries { get; set; }
            public bool IsDryRun { get; set; } = true;
        }

        private CancellationTokenSource cancellationTokenSource;
        private readonly BlockingCollection<FileInfo> imageQueue = new BlockingCollection<FileInfo>();
        private SqliteConnection sqliteConnection;
        private string importDestinationPath;
        private DateTime session_timestamp;
        private ILog log;
        private const String tag = "Data";
        private List<string> extensions;
        private MD5Cache md5Cache;
        private bool deleteSourceAfterImport;
        private readonly QualityFilterMode qualityFilterMode;
        public uint InsertedImageCount { get; private set; }
        public int AlreadyImportedFileCounter { get; private set; }
        public uint DeletedSourceFileCount { get; private set; }
        public uint DeleteSourceFailedCount { get; private set; }
        public IReadOnlyList<string> ImportedFiles => importedFiles.AsReadOnly();
        public IReadOnlyList<string> NotImportedFiles => notImportedFiles.AsReadOnly();
        public IReadOnlyList<QualityAssessmentResult> QualityAssessmentResults => qualityAssessmentResults.AsReadOnly();
        public int QualityReviewCount { get; private set; }
        public int QualityRejectedCount { get; private set; }
        public int QualityErrorCount { get; private set; }
        public ImagesTable Images { get; private set; }
        public PersonsTable Persons { get; private set; }
        public ImageFacesTable ImageFaces { get; private set; }
        public PersonProfilesTable PersonProfiles { get; private set; }
        public PersonPredictionsTable PersonPredictions { get; private set; }
        private readonly List<string> importedFiles = new List<string>();
        private readonly List<string> notImportedFiles = new List<string>();
        private readonly List<QualityAssessmentResult> qualityAssessmentResults = new List<QualityAssessmentResult>();
        private bool disposed;
        public Database(string importDestinationPath, string databaseFilepath, CancellationTokenSource cts, ILog log, List<string> extensions, MD5Cache cache, bool deleteSourceAfterImport = false, QualityFilterMode qualityFilterMode = QualityFilterMode.Off)
        {
            cancellationTokenSource = cts;
            sqliteConnection = new SqliteConnection(databaseFilepath);
            sqliteConnection.Open();
            Images = new ImagesTable(sqliteConnection, log);
            Persons = new PersonsTable(sqliteConnection);
            ImageFaces = new ImageFacesTable(sqliteConnection);
            PersonProfiles = new PersonProfilesTable(sqliteConnection);
            PersonPredictions = new PersonPredictionsTable(sqliteConnection);
            this.importDestinationPath = importDestinationPath;
            session_timestamp = DateTime.Now;
            this.log = log;
            this.extensions = extensions;
            md5Cache = cache;
            this.deleteSourceAfterImport = deleteSourceAfterImport;
            this.qualityFilterMode = qualityFilterMode;
        }
        ~Database()
        {
            Dispose(false);
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

            if (sqliteConnection != null)
            {
                if (sqliteConnection.State != ConnectionState.Closed)
                    sqliteConnection.Close();
                sqliteConnection.Dispose();
            }

            imageQueue.Dispose();

            disposed = true;
        }
        public void OnAddFile(object obj, FileFoundEventArgs args)
        {
            if (!imageQueue.IsAddingCompleted)
                imageQueue.Add(args.FileInfo);
        }

        public void CompleteReceiving()
        {
            if (!imageQueue.IsAddingCompleted)
                imageQueue.CompleteAdding();
        }
        public int GetImageQueueSize()
        {
            return imageQueue.Count;
        }
        public List<string> GetDuplicates()
        {
            // return duplicateList;
            return null;
        }
        public void StartReceiving()
        {
            var utils = new Utils();
            try
            {
                foreach (var item in imageQueue.GetConsumingEnumerable(cancellationTokenSource.Token))
                {
                    try
                    {
                        var extension = item.Extension.ToLower().Trim('.');
                        bool imported;

                        if ((extension.Equals("jpeg") || extension.Equals("jpg")) && extensions.Contains(extension))
                        {
                            var notImportedBefore = notImportedFiles.Count;
                            imported = OnJpegExtension(item, utils);
                            if (!imported && notImportedFiles.Count == notImportedBefore)
                                AddNotImported(item.FullName, "already imported, cache hit, or filtered by quality");
                        }
                        else if (extension.Equals("mp4") && extensions.Contains(extension))
                        {
                            imported = OnMp4Extension(item, utils);
                            if (!imported)
                                AddNotImported(item.FullName, "already imported or cache hit");
                        }
                        else
                        {
                            AddNotImported(item.FullName, "unsupported extension");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddNotImported(item.FullName, "processing error");
                        log.PrintError(tag, ex.Message);
                        log.PrintError(tag, ex.StackTrace);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        private bool CopyAndInsertFile(FileInfo item, Utils utils, string md5, string targetPath, DateTime creationTime, QualityAssessmentResult qualityAssessment = null)
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

            if (!canInsert || !canCopy)
                return false;

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
                        Images.Insert(targetPath, creationTime, md5, qualityAssessment);
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
                    AddImported(item.FullName, targetPath);

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

                    return true;
                }
            }

            return false;
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
                        try
                        {
                            dirPath = utils.CreateDirectoryPathFrom(item.CreationTime);
                        }
                        catch (Exception creationEx)
                        {
                            log.PrintError(tag, "File creation time also invalid, falling back to current date: " + item.FullName);
                            log.PrintError(tag, creationEx.Message);
                            dirPath = utils.CreateDirectoryPathFrom(DateTime.Now);
                        }
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

                string targetPath = Path.Combine(dirPath, fileName).Replace('\\', '/');
                QualityAssessmentResult qualityAssessment = null;

                if (qualityFilterMode != QualityFilterMode.Off)
                {
                    if (QualityAnalyzer.TryAnalyzeJpeg(item.FullName, out var assessment))
                    {
                        qualityAssessment = assessment;
                        qualityAssessment.TargetRelativePath = targetPath;

                        if (assessment.Verdict == QualityVerdict.Review)
                            QualityReviewCount++;

                        if (assessment.Verdict == QualityVerdict.Reject)
                            QualityRejectedCount++;

                        if (qualityFilterMode == QualityFilterMode.Strict && assessment.Verdict == QualityVerdict.Reject)
                        {
                            qualityAssessment.WasImported = false;
                            qualityAssessmentResults.Add(qualityAssessment);
                            AddNotImported(item.FullName, "quality rejected: " + assessment.Reason);
                            log.PrintInfo(tag, "Quality reject: {0} ({1})", item.FullName, assessment.Reason);
                            return false;
                        }

                        if (assessment.Verdict == QualityVerdict.Review || assessment.Verdict == QualityVerdict.Reject)
                        {
                            log.PrintInfo(tag, "Quality review: {0} ({1})", item.FullName, assessment.Reason);
                        }
                    }
                    else
                    {
                        QualityErrorCount++;
                        assessment.TargetRelativePath = targetPath;
                        qualityAssessment = assessment;
                        qualityAssessmentResults.Add(assessment);
                        log.PrintError(tag, "Quality analysis failed for {0}: {1}", item.FullName, assessment.Reason);
                    }
                }

                var imported = CopyAndInsertFile(item, utils, md5, targetPath, creationTime, qualityAssessment);
                if (qualityAssessment != null)
                {
                    qualityAssessment.WasImported = imported;
                    if (!qualityAssessmentResults.Contains(qualityAssessment))
                        qualityAssessmentResults.Add(qualityAssessment);
                }

                return imported;
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
                return CopyAndInsertFile(item, utils, md5, targetPath, creationTime, null);
            }
            return false;
        }

        private void AddImported(string sourcePath, string targetPath)
        {
            importedFiles.Add(sourcePath + " -> " + targetPath);
        }

        private void AddNotImported(string sourcePath, string reason)
        {
            notImportedFiles.Add(sourcePath + " (" + reason + ")");
        }

        public SanityCheckReport RunSanityCheck(bool dryRun)
        {
            var report = new SanityCheckReport { IsDryRun = dryRun };
            if (string.IsNullOrWhiteSpace(importDestinationPath))
                return report;

            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in extensions)
            {
                supportedExtensions.Add(extension.Trim('.'));
            }

            var filesystemFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allFiles = Directory.EnumerateFiles(importDestinationPath, "*", SearchOption.AllDirectories);
            foreach (var fullPath in allFiles)
            {
                var relativePath = Path.GetRelativePath(importDestinationPath, fullPath);
                if (relativePath.StartsWith(".picmag" + Path.DirectorySeparatorChar) || relativePath.Equals(".picmag"))
                    continue;

                var extension = Path.GetExtension(fullPath).Trim('.').ToLower();
                if (!supportedExtensions.Contains(extension))
                    continue;

                filesystemFiles[relativePath.Replace('\\', '/')] = fullPath;
            }

            var databasePaths = Images.GetAllPaths();
            var databasePathSet = new HashSet<string>(databasePaths, StringComparer.OrdinalIgnoreCase);
            var utils = dryRun ? null : new Utils();

            foreach (var dbPath in databasePaths)
            {
                if (!filesystemFiles.ContainsKey(dbPath))
                {
                    report.OrphanDatabaseEntries.Add(dbPath);

                    if (!dryRun)
                    {
                        try
                        {
                            var removed = Images.RemoveByPath(dbPath);
                            if (removed > 0)
                            {
                                report.RemovedDatabaseEntries += removed;
                                log.PrintInfo(tag, "Sanity check removed DB entry: {0}", dbPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.PrintError(tag, ex.Message);
                            log.PrintError(tag, ex.StackTrace);
                        }
                    }
                }
            }

            foreach (var fileEntry in filesystemFiles)
            {
                var relativePath = fileEntry.Key;

                if (databasePathSet.Contains(relativePath))
                    continue;

                report.MissingDatabaseEntries.Add(relativePath);

                if (!dryRun)
                {
                    try
                    {
                        var fullPath = fileEntry.Value;
                        var created = File.GetCreationTime(fullPath);
                        var md5 = BitConverter.ToString(utils.GetMd5(fullPath));
                        Images.Insert(relativePath, created, md5);
                        report.InsertedDatabaseEntries++;
                        log.PrintInfo(tag, "Sanity check inserted DB entry: {0}", relativePath);
                    }
                    catch (Exception ex)
                    {
                        log.PrintError(tag, ex.Message);
                        log.PrintError(tag, ex.StackTrace);
                    }
                }
            }

            log.PrintDebug(tag, "Sanity check finished. Dry-run: {0}, missing DB entries: {1}, orphan DB entries: {2}, inserted DB entries: {3}, removed DB entries: {4}",
                dryRun,
                report.MissingDatabaseEntries.Count,
                report.OrphanDatabaseEntries.Count,
                report.InsertedDatabaseEntries,
                report.RemovedDatabaseEntries);
            return report;
        }

        public SanityCheckReport RunSanityCheckDryRun()
        {
            return RunSanityCheck(true);
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
