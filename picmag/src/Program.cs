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

namespace picmag
{
    partial class Program
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
            Console.WriteLine("\t-i <source path> <target path> [extensions] [--delete-source] [--quality-filter off|warn|strict] [--quality-report] - Import files (default extensions: jpg,mp4)");
            Console.WriteLine("\t   Warning: --delete-source removes source files only after successful import.");
            Console.WriteLine("\t   Quality filter: off (default), warn (flag for review), strict (skip hard fails). --quality-report writes a per-file quality report.");
            Console.WriteLine("\t--sanity-checks <target path> [extensions] [--dry-run|--apply-changes] - Check DB/filesystem consistency and write report (default: --dry-run, extensions: jpg,mp4)");
            Console.WriteLine("\t--migrate-cache <target path> - Migrate .picmag/cache.txt to current format and create .bak backup");
            Console.WriteLine("\t--quality-review <target path> [--verdict review|reject] [--action list|delete|interactive] [--dry-run|--apply-changes] - Review imported files from DB quality metadata (default verdict: reject)");
            Console.WriteLine("\t--quality-scan-existing <target path> [--only-missing|--all] [--dry-run|--apply-changes] - Analyze already imported JPG/JPEG files and store quality metadata in DB");
            Console.WriteLine("\t--person-scan-existing <target path> [--only-missing|--all] - Detect faces and persist embeddings for imported JPG/JPEG files (bundled version-RFB-320.onnx, override via PICMAG_FACE_DETECTION_MODEL)");
            Console.WriteLine("\t--person-add <target path> <name> - Create or reuse a person identity for manual labeling");
            Console.WriteLine("\t--person-list <target path> - List all known persons");
            Console.WriteLine("\t--person-label <target path> [--limit N] | --face-id <id> (--person <name>|--reject) - List unlabeled faces or assign a label");
            Console.WriteLine("\t--person-search <target path> <name> - List image paths with confirmed labels for the person");
            Console.WriteLine("\t--person-train <target path> - Build per-person embedding profiles from confirmed labels");
            Console.WriteLine("\t--person-predict <target path> [--limit <n>] [--min-confidence <0.0-1.0>] - Match unlabeled faces against profiles");
            Console.WriteLine("\t--person-review <target path> <prediction id> (--accept|--reject) - Accept or reject a prediction from --person-predict");
            Console.WriteLine("\t--schedule-import <source path> <target path> [extensions] [--delete-source] [--quality-filter off|warn|strict] [--quality-report] [--before-command \"cmd\"] --period daily|weekly [--time HH:mm] [--weekday mon..sun] - Install/refresh user systemd timer for periodic import");
            Console.WriteLine("\t--unschedule-import - Disable/remove user systemd timer and service for periodic import");
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


        static void Main(string[] args)
        {
            new Program(new FileLog(
                System.IO.Path.Combine(
                    System.Environment.CurrentDirectory,
                    programName + ".log"))).Start(args);
        }
    }
}
