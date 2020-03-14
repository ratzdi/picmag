using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace picmag
{
    class Program
    {
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
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(executingAssembly .Location);
            Console.WriteLine("Usage of {0} v{1}:", fileVersionInfo.ProductName, fileVersionInfo.FileVersion);
            Console.WriteLine("\t-d <DB filepath> <output filepath> - Find duplicates and write results to file.");
            Console.WriteLine("\t-i <source path> <target path> - Import images");
            Console.WriteLine("\t-h help");
        }

        void HandleFindDuplicates(string dbFilepath, string resultFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log);
            var count = database.Images.FindDuplicates();
            log.PrintDebug(tag, "Find Duplicates: number of duplicates " + count);
        }

        void HandleCreateDatabase(string dbFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log);
            var imagesTable = database.Images;
            imagesTable.Create();
            log.PrintDebug(tag, "Main: create Database: Sqlite database created.");
        }

        void HandleImportImages(string databasePath, string sourcePath, string destinationPath)
        {
            Task importTask, databaseTask;
            var importTaskCancellationTokenSource = new CancellationTokenSource();
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();

            var database = new Database(destinationPath, "URI=file:" + databasePath, databaseTaskCancellationTokenSource, log);

            databaseTask = new Task(new Action(database.StartReceiving), databaseTaskCancellationTokenSource.Token);
            databaseTask.Start();

            var imageFinder = new ImageImport(importTaskCancellationTokenSource, sourcePath);
            imageFinder.AddFile += database.OnAddFile;
            importTask = new Task(new Action(imageFinder.Start), importTaskCancellationTokenSource.Token);
            importTask.Start();

            importTask.Wait();
            while (database.GetImageQueueSize() > 0)
            {
                log.PrintDebug(tag, "Main: wait for Database: {0}", database.GetImageQueueSize());
                Thread.Sleep(3000);
            }

            databaseTaskCancellationTokenSource.Cancel();

            databaseTask.Wait();

            log.PrintDebug(tag, "Main: files found in root directory " + imageFinder.TotalFilesCount);
            log.PrintDebug(tag, "Main: files inserted to database " + database.InsertedImageCount);
        }

        void Start(string []args)
        {
            if(args.Length == 0 || args[0] == "-h")
            {
                PrintUsage();
                return;
            }

            if (args[0] == "-d")
            {
                if (args.Length == 3)
                {
                    log.PrintDebug(tag, "Main: database filepath: " + args[1]);
                    log.PrintDebug(tag, "Main: Result filepath: " + args[2]);
                    HandleFindDuplicates(args[1], args[2]);
                }
                else
                {
                    PrintUsage();
                }
            }
            else if (args[0] == "-i")
            {
                if (args.Length == 3)
                {
                    var databaseFullpath = System.IO.Path.Combine(args[2], relDatabaseFilepath);
                    if (!System.IO.File.Exists(databaseFullpath))
                    {
                        var utils = new Utils();
                        utils.CreateDirectoryPath(databaseFullpath);
                        HandleCreateDatabase(databaseFullpath);
                    }
                    log.PrintDebug(tag, "Main: Database filepath: {0}", databaseFullpath);
                    log.PrintDebug(tag, "Main: Image source path: {0}", args[1]);
                    log.PrintDebug(tag, "Main: Image destination path: {0}", args[2]);
                    HandleImportImages(databaseFullpath, args[1], args[2]);
                }
                else
                {
                    PrintUsage();
                }
            }
        }

        static void Main(string[] args)
        {
            new Program(new Log()).Start(args);
        }
    }
}
