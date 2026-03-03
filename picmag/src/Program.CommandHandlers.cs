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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace picmag
{
    partial class Program
    {
        void HandleFindDuplicates(string dbFilepath, string resultFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            using var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log, null, null);
            var count = database.Images.FindDuplicates();
            log.PrintDebug(tag, "number of duplicates " + count);
        }

        void HandleCreateDatabase(string dbFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            using var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log, null, null);
            var imagesTable = database.Images;
            imagesTable.Create();
            log.PrintDebug(tag, "Sqlite database created.");
        }

        void HandleImportImages(string databasePath, string sourcePath, string destinationPath, List<string> extensions, bool deleteSourceAfterImport, QualityFilterMode qualityFilterMode, bool writeQualityReport)
        {
            DateTime t1 = DateTime.Now;
            Task importTask, databaseTask;
            var importTaskCancellationTokenSource = new CancellationTokenSource();
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            using var md5Cache = new MD5Cache(System.IO.Path.Combine(destinationPath, ".picmag", "cache.txt"), log);
            using var database = new Database(destinationPath, "URI=file:" + databasePath, databaseTaskCancellationTokenSource, log, extensions, md5Cache, deleteSourceAfterImport, qualityFilterMode);

            databaseTask = new Task(new Action(database.StartReceiving), databaseTaskCancellationTokenSource.Token);
            databaseTask.Start();

            var imageFinder = new ImageImport(importTaskCancellationTokenSource, sourcePath);
            imageFinder.AddFile += database.OnAddFile;
            importTask = new Task(new Action(imageFinder.Start), importTaskCancellationTokenSource.Token);
            importTask.Start();

            importTask.Wait();
            database.CompleteReceiving();

            databaseTask.Wait();

            var importDuration = (DateTime.Now - t1);

            log.PrintDebug(tag, "Files found in source path: " + imageFinder.TotalFilesCount);
            log.PrintDebug(tag, "Files imported: " + database.InsertedImageCount);
            log.PrintDebug(tag, "Source files deleted: " + database.DeletedSourceFileCount);
            log.PrintDebug(tag, "Source file delete failures: " + database.DeleteSourceFailedCount);
            log.PrintDebug(tag, "Quality filter mode: {0}", qualityFilterMode);
            log.PrintDebug(tag, "Quality reviewed files: {0}", database.QualityReviewCount);
            log.PrintDebug(tag, "Quality rejected files: {0}", database.QualityRejectedCount);
            log.PrintDebug(tag, "Quality analysis errors: {0}", database.QualityErrorCount);
            log.PrintDebug(tag, "Process took: " + importDuration.ToString());

            WriteImportSummary(destinationPath, imageFinder.TotalFilesCount, database, importDuration, qualityFilterMode);

            if (writeQualityReport)
                WriteQualityReport(destinationPath, database, qualityFilterMode);
        }

        void WriteImportSummary(string destinationPath, uint totalFilesCount, Database database, TimeSpan duration, QualityFilterMode qualityFilterMode)
        {
            try
            {
                var summaryDirectory = Path.Combine(destinationPath, ".picmag");
                Directory.CreateDirectory(summaryDirectory);
                var summaryFilePath = Path.Combine(summaryDirectory, $"import-summary-{DateTime.Now:yyyyMMdd-HHmmss}.log");

                var content = new StringBuilder();
                content.AppendLine("Import Summary");
                content.AppendLine($"Generated at: {DateTime.Now:O}");
                content.AppendLine($"Number of scanned files: {totalFilesCount}");
                content.AppendLine($"Number of imported files: {database.InsertedImageCount}");
                content.AppendLine($"Number of not imported files: {database.NotImportedFiles.Count}");
                content.AppendLine($"Quality filter mode: {qualityFilterMode.ToString().ToLowerInvariant()}");
                content.AppendLine($"Quality reviewed files: {database.QualityReviewCount}");
                content.AppendLine($"Quality rejected files: {database.QualityRejectedCount}");
                content.AppendLine($"Quality analysis errors: {database.QualityErrorCount}");
                content.AppendLine($"Process duration: {duration}");
                content.AppendLine();
                content.AppendLine("List of imported files:");
                foreach (var importedFile in database.ImportedFiles)
                {
                    content.AppendLine(importedFile);
                }

                content.AppendLine();
                content.AppendLine("List of not imported files:");
                foreach (var notImportedFile in database.NotImportedFiles)
                {
                    content.AppendLine(notImportedFile);
                }

                File.WriteAllText(summaryFilePath, content.ToString());
                log.PrintDebug(tag, "Import summary written to: {0}", summaryFilePath);
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Failed to write import summary: {0}", ex.Message);
            }
        }

        void WriteQualityReport(string destinationPath, Database database, QualityFilterMode qualityFilterMode)
        {
            try
            {
                var reportDirectory = Path.Combine(destinationPath, ".picmag");
                Directory.CreateDirectory(reportDirectory);
                var reportFilePath = Path.Combine(reportDirectory, $"quality-report-{DateTime.Now:yyyyMMdd-HHmmss}.log");

                var content = new StringBuilder();
                content.AppendLine("Quality Report");
                content.AppendLine($"Generated at: {DateTime.Now:O}");
                content.AppendLine($"Mode: {qualityFilterMode.ToString().ToLowerInvariant()}");
                content.AppendLine($"Assessed files: {database.QualityAssessmentResults.Count}");
                content.AppendLine($"Reviewed files: {database.QualityReviewCount}");
                content.AppendLine($"Rejected files: {database.QualityRejectedCount}");
                content.AppendLine($"Analysis errors: {database.QualityErrorCount}");
                content.AppendLine();
                content.AppendLine("Entries:");

                foreach (var entry in database.QualityAssessmentResults)
                {
                    content.AppendLine($"{entry.SourcePath} :: {entry.ToSummary()}");
                }

                File.WriteAllText(reportFilePath, content.ToString());
                log.PrintDebug(tag, "Quality report written to: {0}", reportFilePath);

                var jsonReportFilePath = Path.ChangeExtension(reportFilePath, ".json");
                var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
                serializerOptions.Converters.Add(new JsonStringEnumConverter());
                File.WriteAllText(jsonReportFilePath, JsonSerializer.Serialize(database.QualityAssessmentResults, serializerOptions));
                log.PrintDebug(tag, "Quality report JSON written to: {0}", jsonReportFilePath);
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Failed to write quality report: {0}", ex.Message);
            }
        }

        void HandleQualityReview(string targetPath, QualityReviewVerdict verdict, QualityReviewAction action, bool dryRun)
        {
            var picmagDirectory = Path.Combine(targetPath, ".picmag");
            Directory.CreateDirectory(picmagDirectory);

            string databaseFullpath = Path.Combine(targetPath, relDatabaseFilepath);
            if (!File.Exists(databaseFullpath))
            {
                log.PrintError(tag, "Database not found: {0}", databaseFullpath);
                return;
            }

            using var databaseCts = new CancellationTokenSource();
            using var md5Cache = new MD5Cache(Path.Combine(targetPath, ".picmag", "cache.txt"), log);
            using var database = new Database(targetPath, "URI=file:" + databaseFullpath, databaseCts, log, new List<string> { "jpg", "mp4" }, md5Cache);

            var candidates = database.Images.GetByQualityVerdict(verdict);

            int deletedFiles = 0;
            int removedDbEntries = 0;
            int missingFiles = 0;
            int keptFiles = 0;
            bool quitRequested = false;

            foreach (var candidate in candidates)
            {
                var relativePath = candidate.Path.Replace('\\', '/');
                var absolutePath = Path.Combine(targetPath, relativePath);

                if (action == QualityReviewAction.List)
                {
                    log.PrintInfo(tag, "Quality candidate: {0} ({1})", relativePath, candidate.QualityReason ?? "n/a");
                    continue;
                }

                if (action == QualityReviewAction.Delete)
                {
                    ProcessDeleteDecision(database, relativePath, absolutePath, dryRun, ref deletedFiles, ref removedDbEntries, ref missingFiles);
                    continue;
                }

                if (action == QualityReviewAction.Interactive)
                {
                    TryOpenImageWindow(absolutePath);

                    Console.WriteLine();
                    Console.WriteLine("Quality review candidate: {0}", relativePath);
                    Console.WriteLine("Reason: {0}", candidate.QualityReason ?? "n/a");
                    Console.WriteLine("Decision [d=delete, k=keep, q=quit]: ");
                    var decision = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

                    if (decision == "q")
                    {
                        quitRequested = true;
                        break;
                    }

                    if (decision == "d")
                    {
                        ProcessDeleteDecision(database, relativePath, absolutePath, dryRun, ref deletedFiles, ref removedDbEntries, ref missingFiles);
                    }
                    else
                    {
                        keptFiles++;
                        log.PrintInfo(tag, "Kept file: {0}", relativePath);
                    }
                }
            }

            var reviewReportPath = Path.Combine(picmagDirectory, $"quality-review-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var report = new StringBuilder();
            report.AppendLine("Quality Review Report");
            report.AppendLine($"Generated at: {DateTime.Now:O}");
            report.AppendLine($"Source database: {databaseFullpath}");
            report.AppendLine($"verdict: {verdict.ToString().ToLowerInvariant()}");
            report.AppendLine($"action: {action.ToString().ToLowerInvariant()}");
            report.AppendLine($"mode: {(dryRun ? "dry-run" : "apply-changes")}");
            report.AppendLine($"matching_entries: {candidates.Count}");
            report.AppendLine($"deleted_files: {deletedFiles}");
            report.AppendLine($"removed_db_entries: {removedDbEntries}");
            report.AppendLine($"missing_files: {missingFiles}");
            report.AppendLine($"kept_files: {keptFiles}");
            report.AppendLine($"quit_requested: {quitRequested}");
            report.AppendLine();
            report.AppendLine("Entries:");

            foreach (var candidate in candidates)
            {
                report.AppendLine($"{candidate.Path} :: verdict={candidate.QualityVerdict}, reason={candidate.QualityReason ?? "n/a"}, contrast={candidate.QualityContrast}, sharpness={candidate.QualitySharpness}");
            }

            File.WriteAllText(reviewReportPath, report.ToString());
            log.PrintDebug(tag, "Quality review report written to: {0}", reviewReportPath);
        }

        void HandleQualityScanExisting(string targetPath, bool onlyMissing, bool dryRun)
        {
            var picmagDirectory = Path.Combine(targetPath, ".picmag");
            Directory.CreateDirectory(picmagDirectory);

            string databaseFullpath = Path.Combine(targetPath, relDatabaseFilepath);
            if (!File.Exists(databaseFullpath))
            {
                log.PrintError(tag, "Database not found: {0}", databaseFullpath);
                return;
            }

            using var databaseCts = new CancellationTokenSource();
            using var md5Cache = new MD5Cache(Path.Combine(targetPath, ".picmag", "cache.txt"), log);
            using var database = new Database(targetPath, "URI=file:" + databaseFullpath, databaseCts, log, new List<string> { "jpg", "mp4" }, md5Cache);

            var candidates = database.Images.GetJpegPathsForQualityScan(onlyMissing);

            if (candidates.Count == 0)
            {
                log.PrintInfo(tag, "Quality scan existing: no matching JPG/JPEG entries found.");
            }

            var maxDegreeOfParallelism = Math.Min(Math.Max(Environment.ProcessorCount, 1), 8);
            log.PrintInfo(tag, "Quality scan existing: processing {0} candidates with up to {1} workers.", candidates.Count, maxDegreeOfParallelism);

            var progressStopwatch = Stopwatch.StartNew();
            var lastProgressOutput = TimeSpan.Zero;
            var progressLock = new object();
            var analysisResults = new List<(string RelativePath, QualityAssessmentResult Assessment)>(candidates.Count);
            var analysisResultsLock = new object();

            int assessed = 0;
            int errors = 0;
            int rejected = 0;
            int review = 0;
            int accepted = 0;
            int missingFiles = 0;
            int updatedRows = 0;
            var totalCandidates = candidates.Count;

            Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, relativePath =>
            {
                var absolutePath = Path.Combine(targetPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                QualityAssessmentResult assessment;

                if (!File.Exists(absolutePath))
                {
                    Interlocked.Increment(ref missingFiles);
                    assessment = new QualityAssessmentResult
                    {
                        SourcePath = absolutePath,
                        TargetRelativePath = relativePath,
                        WasImported = true,
                        Verdict = QualityVerdict.Error,
                        Reason = "file missing"
                    };
                }
                else if (!QualityAnalyzer.TryAnalyzeJpeg(absolutePath, out assessment))
                {
                    Interlocked.Increment(ref errors);
                    assessment.TargetRelativePath = relativePath;
                    assessment.WasImported = true;
                }
                else
                {
                    assessment.TargetRelativePath = relativePath;
                    assessment.WasImported = true;
                    switch (assessment.Verdict)
                    {
                        case QualityVerdict.Accept:
                            Interlocked.Increment(ref accepted);
                            break;
                        case QualityVerdict.Review:
                            Interlocked.Increment(ref review);
                            break;
                        case QualityVerdict.Reject:
                            Interlocked.Increment(ref rejected);
                            break;
                        case QualityVerdict.Error:
                            Interlocked.Increment(ref errors);
                            break;
                    }
                }

                lock (analysisResultsLock)
                {
                    analysisResults.Add((relativePath, assessment));
                }

                var currentAssessed = Interlocked.Increment(ref assessed);
                var shouldRenderProgress = (currentAssessed % 25 == 0) || currentAssessed == totalCandidates;
                if (!shouldRenderProgress)
                {
                    lock (progressLock)
                    {
                        shouldRenderProgress = (progressStopwatch.Elapsed - lastProgressOutput).TotalSeconds >= 1.0;
                    }
                }

                if (shouldRenderProgress && totalCandidates > 0)
                {
                    lock (progressLock)
                    {
                        var percent = (int)Math.Round((currentAssessed * 100.0) / totalCandidates);
                        Console.Write("\rQuality scan progress: {0,3}% ({1}/{2})", percent, currentAssessed, totalCandidates);
                        lastProgressOutput = progressStopwatch.Elapsed;
                    }
                }
            });

            if (!dryRun)
            {
                foreach (var analysisResult in analysisResults)
                {
                    try
                    {
                        updatedRows += database.Images.UpdateQualityMetadata(analysisResult.RelativePath, analysisResult.Assessment);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
                        log.PrintError(tag, "Failed to update quality metadata for {0}: {1}", analysisResult.RelativePath, ex.Message);
                    }
                }
            }

            if (candidates.Count > 0)
                Console.WriteLine();

            var scanReportPath = Path.Combine(picmagDirectory, $"quality-scan-existing-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var report = new StringBuilder();
            report.AppendLine("Quality Scan Existing Report");
            report.AppendLine($"Generated at: {DateTime.Now:O}");
            report.AppendLine($"Source database: {databaseFullpath}");
            report.AppendLine($"scan_scope: {(onlyMissing ? "only-missing" : "all")}");
            report.AppendLine($"mode: {(dryRun ? "dry-run" : "apply-changes")}");
            report.AppendLine($"candidates: {candidates.Count}");
            report.AppendLine($"assessed: {assessed}");
            report.AppendLine($"accepted: {accepted}");
            report.AppendLine($"review: {review}");
            report.AppendLine($"rejected: {rejected}");
            report.AppendLine($"errors: {errors}");
            report.AppendLine($"missing_files: {missingFiles}");
            report.AppendLine($"updated_rows: {updatedRows}");

            File.WriteAllText(scanReportPath, report.ToString());
            log.PrintDebug(tag, "Quality scan report written to: {0}", scanReportPath);
            log.PrintInfo(tag, "Quality scan existing completed. candidates={0}, assessed={1}, accepted={2}, review={3}, rejected={4}, errors={5}, mode={6}",
                candidates.Count,
                assessed,
                accepted,
                review,
                rejected,
                errors,
                dryRun ? "dry-run" : "apply-changes");
        }

        void ProcessDeleteDecision(Database database, string relativePath, string absolutePath, bool dryRun, ref int deletedFiles, ref int removedDbEntries, ref int missingFiles)
        {
            if (dryRun)
            {
                log.PrintInfo(tag, "Dry-run delete candidate: {0}", relativePath);
                return;
            }

            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    deletedFiles++;
                    log.PrintInfo(tag, "Deleted file: {0}", relativePath);
                }
                else
                {
                    missingFiles++;
                    log.PrintInfo(tag, "File already missing: {0}", relativePath);
                }

                removedDbEntries += database.Images.RemoveByPath(relativePath);
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Failed to process delete for {0}: {1}", relativePath, ex.Message);
            }
        }

        void TryOpenImageWindow(string absolutePath)
        {
            try
            {
                if (!File.Exists(absolutePath))
                    return;

                var process = new Process();
                process.StartInfo.FileName = "xdg-open";
                process.StartInfo.Arguments = "\"" + absolutePath + "\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
            }
            catch (Exception ex)
            {
                log.PrintDebug(tag, "Could not open image window for {0}: {1}", absolutePath, ex.Message);
            }
        }

        void HandleSanityChecks(string targetPath, List<string> extensions, bool dryRun)
        {
            var databaseFullpath = System.IO.Path.Combine(targetPath, relDatabaseFilepath);
            if (!System.IO.File.Exists(databaseFullpath))
            {
                var utils = new Utils();
                utils.CreateDirectoryPath(databaseFullpath);
                HandleCreateDatabase(databaseFullpath);
            }

            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            using var md5Cache = new MD5Cache(System.IO.Path.Combine(targetPath, ".picmag", "cache.txt"), log);
            using var database = new Database(targetPath, "URI=file:" + databaseFullpath, databaseTaskCancellationTokenSource, log, extensions, md5Cache);

            log.PrintDebug(tag, "Sanity check DB filepath: {0}", databaseFullpath);
            log.PrintDebug(tag, "Sanity check target path: {0}", targetPath);
            log.PrintDebug(tag, "Sanity check dry-run: {0}", dryRun);

            var report = database.RunSanityCheck(dryRun);
            WriteSanityCheckReport(targetPath, extensions, report);
        }

        void HandleMigrateCache(string targetPath)
        {
            var cachePath = Path.Combine(targetPath, ".picmag", "cache.txt");
            try
            {
                var result = MD5Cache.MigrateFile(cachePath, log);
                log.PrintDebug(tag, "Cache migration finished. valid: {0}, legacy: {1}, invalid: {2}", result.ValidEntries, result.LegacyEntries, result.InvalidEntries);
                log.PrintDebug(tag, "Cache path: {0}", cachePath);
                log.PrintDebug(tag, "Cache backup path: {0}", cachePath + ".bak");
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Failed to migrate cache: {0}", ex.Message);
            }
        }

        void WriteSanityCheckReport(string targetPath, List<string> extensions, Database.SanityCheckReport report)
        {
            try
            {
                var reportDirectory = Path.Combine(targetPath, ".picmag");
                Directory.CreateDirectory(reportDirectory);
                var reportFilePath = Path.Combine(reportDirectory, $"sanity-check-{DateTime.Now:yyyyMMdd-HHmmss}.log");

                var content = new StringBuilder();
                content.AppendLine("Sanity Check Report");
                content.AppendLine($"Generated at: {DateTime.Now:O}");
                content.AppendLine($"Target path: {targetPath}");
                content.AppendLine($"Extensions: {string.Join(",", extensions)}");
                content.AppendLine($"Mode: {(report.IsDryRun ? "dry-run" : "apply-changes")}");
                content.AppendLine($"missing_db_entries_count: {report.MissingDatabaseEntries.Count}");
                content.AppendLine($"orphan_db_entries_count: {report.OrphanDatabaseEntries.Count}");
                content.AppendLine($"inserted_db_entries_count: {report.InsertedDatabaseEntries}");
                content.AppendLine($"removed_db_entries_count: {report.RemovedDatabaseEntries}");
                content.AppendLine();

                content.AppendLine("Files missing in DB:");
                foreach (var file in report.MissingDatabaseEntries)
                {
                    content.AppendLine(file);
                }

                content.AppendLine();
                content.AppendLine("DB entries missing on filesystem:");
                foreach (var file in report.OrphanDatabaseEntries)
                {
                    content.AppendLine(file);
                }

                content.AppendLine();
                content.AppendLine(report.IsDryRun ? "No changes applied (dry-run)." : "Changes applied.");

                File.WriteAllText(reportFilePath, content.ToString());
                log.PrintDebug(tag, "Sanity check report written to: {0}", reportFilePath);
            }
            catch (Exception ex)
            {
                log.PrintError(tag, "Failed to write sanity check report: {0}", ex.Message);
            }
        }

        void ExecuteCommand(CommandRequest request)
        {
            switch (request.Type)
            {
                case CommandType.Help:
                    PrintUsage();
                    break;

                case CommandType.Version:
                    PrintVersion();
                    break;

                case CommandType.FindDuplicates:
                    log.PrintDebug(tag, "DB filepath: " + request.DbFilepath);
                    log.PrintDebug(tag, "Target directory: " + request.ResultFilepath);
                    HandleFindDuplicates(request.DbFilepath, request.ResultFilepath);
                    break;

                case CommandType.Import:
                    ExecuteImportCommand(request);
                    break;

                case CommandType.SanityChecks:
                    HandleSanityChecks(request.TargetPath, request.Extensions, request.DryRun);
                    break;

                case CommandType.MigrateCache:
                    HandleMigrateCache(request.TargetPath);
                    break;

                case CommandType.QualityReview:
                    HandleQualityReview(request.TargetPath, request.QualityReviewVerdict, request.QualityReviewAction, request.DryRun);
                    break;

                case CommandType.QualityScanExisting:
                    HandleQualityScanExisting(request.TargetPath, request.QualityScanOnlyMissing, request.DryRun);
                    break;
            }
        }

        void ExecuteImportCommand(CommandRequest request)
        {
            var databaseFullpath = System.IO.Path.Combine(request.TargetPath, relDatabaseFilepath);
            if (!System.IO.File.Exists(databaseFullpath))
            {
                var utils = new Utils();
                utils.CreateDirectoryPath(databaseFullpath);
                HandleCreateDatabase(databaseFullpath);
            }

            log.PrintDebug(tag, "DB filepath: {0}", databaseFullpath);
            log.PrintDebug(tag, "Source path: {0}", request.SourcePath);
            log.PrintDebug(tag, "Target path: {0}", request.TargetPath);
            log.PrintDebug(tag, "Delete source after import: {0}", request.DeleteSourceAfterImport);
            log.PrintDebug(tag, "Quality filter mode: {0}", request.QualityFilterMode);
            log.PrintDebug(tag, "Write quality report: {0}", request.WriteQualityReport);
            HandleImportImages(databaseFullpath, request.SourcePath, request.TargetPath, request.Extensions, request.DeleteSourceAfterImport, request.QualityFilterMode, request.WriteQualityReport);
        }
    }
}
