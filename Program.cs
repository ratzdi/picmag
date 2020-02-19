using System;
using System.Threading;
using System.Threading.Tasks;

namespace picmag
{
    class Program
    {
        private ILog log;
        private const String tag = "Main";
        private Program(ILog log)
        {
            this.log = log;
        }
        void PrintUsage()
        {
            Console.WriteLine("Application Usage:");
            Console.WriteLine("-d <database filepath> <output filepath>");
            Console.WriteLine("\t Find duplicates in database and write results to file.");
            Console.WriteLine("-c <database filepath>");
            Console.WriteLine("\t Creates a new Sqlite database file if no one exists in current directory");
            Console.WriteLine("-i <database filepath> <directory path>");
            Console.WriteLine("\t Find and insert images to database recursively from directory path.");
        }
        void HandleFindDuplicates(string dbFilepath, string resultFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:" + dbFilepath, databaseTaskCancellationTokenSource, log);
            var imageTable = database.Images;
            var count = imageTable.FindDuplicates();
            log.PrintDebug(tag, "Find Duplicates: number of duplicates " + count);
        }
        void HandleCreateDatabase(string dbFilepath)
        {
            var databaseTaskCancellationTokenSource = new CancellationTokenSource();
            var database = new Database(null, "URI=file:SqliteTest.db", databaseTaskCancellationTokenSource, log);
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
        void _Main(string []args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
            }
            else
            {
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
                else if (args[0] == "-c")
                {
                    if (args.Length == 2)
                    {
                        log.PrintDebug(tag, "Main: Create new database...");
                        log.PrintDebug(tag, "Main: Database filepath: " + args[1]);
                        HandleCreateDatabase(args[1]);
                    }
                    else
                    {
                        PrintUsage();
                    }
                }
                else if (args[0] == "-i")
                {
                    if (args.Length == 4)
                    {
                        log.PrintDebug(tag, "Main: Database filepath: " + args[1]);
                        log.PrintDebug(tag, "Main: Image source path: " + args[2]);
                        log.PrintDebug(tag, "Main: Image destination path: " + args[3]);
                        HandleImportImages(args[1], args[2], args[3]);
                    }
                    else
                    {
                        PrintUsage();
                    }
                }
            }
        }
        static void Main(string[] args)
        {
            new Program(new Log())._Main(args);
        }
    }
}
