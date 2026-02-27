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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace picmag
{
    class Program
    {
        private const String programName = "picmag";
        private ILog log;
        private const String tag = "Main";
        private readonly String relDatabaseFilepath = System.IO.Path.Combine(".picmag", "database.sqlite");

        private Program(ILog log)
        {
            this.log = log;
        }

        void PrintUsage()
        {
            var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(executingAssembly.Location);
            Console.WriteLine("Usage of {0} v{1}:", fileVersionInfo.ProductName, fileVersionInfo.FileVersion);
            Console.WriteLine("\t-d <DB filepath> <output filepath> - Find duplicates and write results to a file and to the std output.");
            Console.WriteLine("\t-i <source path> <target path> [extensions] [--delete-source] - Import files (default extensions: jpg,mp4)");
            Console.WriteLine("\t   Warning: --delete-source removes source files only after successful import.");
            Console.WriteLine("\t--sanity-checks <target path> [extensions] [--dry-run|--apply-changes] - Check DB/filesystem consistency and write report (default: --dry-run, extensions: jpg,mp4)");
            Console.WriteLine("\t--migrate-cache <target path> - Migrate .picmag/cache.txt to current format and create .bak backup");
            Console.WriteLine("\t--version, -v - Print application version and git short revision");
            Console.WriteLine("\t-h help");
        }

        string GetAppVersion()
        {
            var versionAttribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (versionAttribute != null && !string.IsNullOrWhiteSpace(versionAttribute.InformationalVersion))
            {
                return versionAttribute.InformationalVersion.Split('+')[0];
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
                return "unknown";

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        string FindGitRepositoryRoot()
        {
            var candidateStartPaths = new List<string> {
                Environment.CurrentDirectory,
                AppContext.BaseDirectory
            };

            foreach (var startPath in candidateStartPaths)
            {
                if (string.IsNullOrWhiteSpace(startPath))
                    continue;

                var directory = new DirectoryInfo(startPath);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                        return directory.FullName;
                    directory = directory.Parent;
                }
            }

            return null;
        }

        string GetGitShortRevision()
        {
            try
            {
                var repositoryRoot = FindGitRepositoryRoot();
                if (string.IsNullOrWhiteSpace(repositoryRoot))
                    return "unknown";

                var process = new Process();
                process.StartInfo.FileName = "git";
                process.StartInfo.Arguments = "rev-parse --short HEAD";
                process.StartInfo.WorkingDirectory = repositoryRoot;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return output.Trim();
            }
            catch
            {
            }

            return "unknown";
        }

        void PrintVersion()
        {
            var version = GetAppVersion();
            var revision = GetGitShortRevision();
            Console.WriteLine("{0} {1} ({2})", programName, version, revision);
        }

        void HandleFindDuplicates(string dbFilepath, string resultFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log, null, null);
            var count = database.Images.FindDuplicates();
            log.PrintDebug(tag, "number of duplicates " + count);
        }

        void HandleCreateDatabase(string dbFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log, null, null);
            var imagesTable = database.Images;
            imagesTable.Create();
            log.PrintDebug(tag, "Sqlite database created.");
        }

        void HandleImportImages(string databasePath, string sourcePath, string destinationPath, List<string> extensions, bool deleteSourceAfterImport)
        {
            DateTime t1 = DateTime.Now;
            Task importTask, databaseTask;
            var importTaskCancellationTokenSource = new CancellationTokenSource();
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            using var md5Cache = new MD5Cache(System.IO.Path.Combine(destinationPath, ".picmag", "cache.txt"), log);
            var database = new Database(destinationPath, "URI=file:" + databasePath, databaseTaskCancellationTokenSource, log, extensions, md5Cache, deleteSourceAfterImport);

            databaseTask = new Task(new Action(database.StartReceiving), databaseTaskCancellationTokenSource.Token);
            databaseTask.Start();

            var imageFinder = new ImageImport(importTaskCancellationTokenSource, sourcePath);
            imageFinder.AddFile += database.OnAddFile;
            importTask = new Task(new Action(imageFinder.Start), importTaskCancellationTokenSource.Token);
            importTask.Start();

            importTask.Wait();
            while (database.GetImageQueueSize() > 0)
            {
                log.PrintDebug(tag, "DB task buzy with images: {0}", database.GetImageQueueSize());
                Thread.Sleep(3000);
            }

            databaseTaskCancellationTokenSource.Cancel();

            databaseTask.Wait();

            var importDuration = (DateTime.Now - t1);

            log.PrintDebug(tag, "Files found in source path: " + imageFinder.TotalFilesCount);
            log.PrintDebug(tag, "Files imported: " + database.InsertedImageCount);
            log.PrintDebug(tag, "Source files deleted: " + database.DeletedSourceFileCount);
            log.PrintDebug(tag, "Source file delete failures: " + database.DeleteSourceFailedCount);
            log.PrintDebug(tag, "Process took: " + importDuration.ToString());

            WriteImportSummary(destinationPath, imageFinder.TotalFilesCount, database, importDuration);
        }

        void WriteImportSummary(string destinationPath, uint totalFilesCount, Database database, TimeSpan duration)
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
            var database = new Database(targetPath, "URI=file:" + databaseFullpath, databaseTaskCancellationTokenSource, log, extensions, md5Cache);

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

        void Start(string[] args)
        {
            if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
            {
                PrintVersion();
                return;
            }

            if (args.Length == 0 || args[0] == "-h")
            {
                PrintUsage();
                return;
            }

            if (args[0] == "-d")
            {
                if (args.Length == 3)
                {
                    log.PrintDebug(tag, "DB filepath: " + args[1]);
                    log.PrintDebug(tag, "Target directory: " + args[2]);
                    HandleFindDuplicates(args[1], args[2]);
                }
                else
                {
                    PrintUsage();
                }
            }
            else if (args[0] == "-i")
            {
                if (args.Length >= 3)
                {
                    var deleteSourceAfterImport = false;
                    var extensions = new List<string> { "jpg", "mp4" };

                    for (int i = 3; i < args.Length; i++)
                    {
                        if (args[i] == "--delete-source")
                        {
                            deleteSourceAfterImport = true;
                        }
                        else if (args[i].StartsWith("-"))
                        {
                            PrintUsage();
                            return;
                        }
                        else
                        {
                            if (extensions.Count == 2 && extensions[0] == "jpg" && extensions[1] == "mp4")
                            {
                                extensions = new List<string>(args[i].ToLower().Split(','));
                            }
                            else
                            {
                                PrintUsage();
                                return;
                            }
                        }
                    }

                    var databaseFullpath = System.IO.Path.Combine(args[2], relDatabaseFilepath);
                    if (!System.IO.File.Exists(databaseFullpath))
                    {
                        var utils = new Utils();
                        utils.CreateDirectoryPath(databaseFullpath);
                        HandleCreateDatabase(databaseFullpath);
                    }
                    log.PrintDebug(tag, "DB filepath: {0}", databaseFullpath);
                    log.PrintDebug(tag, "Source path: {0}", args[1]);
                    log.PrintDebug(tag, "Target path: {0}", args[2]);
                    log.PrintDebug(tag, "Delete source after import: {0}", deleteSourceAfterImport);
                    HandleImportImages(databaseFullpath, args[1], args[2], extensions, deleteSourceAfterImport);
                }
                else
                {
                    PrintUsage();
                }
            }
            else if (args[0] == "--sanity-checks")
            {
                if (args.Length >= 2)
                {
                    var extensions = new List<string> { "jpg", "mp4" };
                    var dryRun = true;

                    for (int i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--apply-changes")
                        {
                            dryRun = false;
                        }
                        else if (args[i] == "--dry-run")
                        {
                            dryRun = true;
                        }
                        else if (args[i].StartsWith("-"))
                        {
                            PrintUsage();
                            return;
                        }
                        else
                        {
                            if (extensions.Count == 2 && extensions[0] == "jpg" && extensions[1] == "mp4")
                            {
                                extensions = new List<string>(args[i].ToLower().Split(','));
                            }
                            else
                            {
                                PrintUsage();
                                return;
                            }
                        }
                    }

                    HandleSanityChecks(args[1], extensions, dryRun);
                }
                else
                {
                    PrintUsage();
                }
            }
            else if (args[0] == "--migrate-cache")
            {
                if (args.Length == 2)
                {
                    HandleMigrateCache(args[1]);
                }
                else
                {
                    PrintUsage();
                }
            }
        }

        static void Main(string[] args)
        {
            new Program(new FileLog(
                System.IO.Path.Combine(
                    System.Environment.CurrentDirectory,
                    programName + ".log"))).Start(args);
        }
    }
}
