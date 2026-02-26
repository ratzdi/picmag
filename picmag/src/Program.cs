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
            Console.WriteLine("\t-i <source path> <target path> [extensions] [--delete-source] - Import files (default extension: jpg)");
            Console.WriteLine("\t   Warning: --delete-source removes source files only after successful import.");
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
            var md5Cache = new MD5Cache(System.IO.Path.Combine(destinationPath, ".picmag", "cache.txt"), log);
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
                    var extensions = new List<string> { "jpg" };

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
                            if (extensions.Count == 1 && extensions[0] == "jpg")
                            {
                                extensions = new List<string>(args[i].Split(','));
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
