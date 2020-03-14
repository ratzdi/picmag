
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mono.Data.Sqlite;
using System.Data;

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
        public int InsertedImageCount { get; private set; }
        public ImagesTable Images { get; private set; }
        public Database(string importDestinationPath, string databaseFilepath, CancellationTokenSource cts, ILog log)
        {
            cancellationTokenSource = cts;
            sqliteConnection = new SqliteConnection(databaseFilepath);
            sqliteConnection.Open();
            Images = new ImagesTable(sqliteConnection, log);
            this.importDestinationPath = importDestinationPath;
            session_timestamp = DateTime.Now;
            this.log = log;
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
                        log.PrintDebug(tag, "{0}", item.Name);

                        byte[] md5 = null;
                        string targetPath = string.Empty;
                        DateTime creationTime;
                        string fileName = string.Empty;

                        switch (item.Extension.ToLower())
                        {
                            case ".jpg":
                            case ".jpeg":
                                string dirPath = string.Empty;
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
                                    md5 = jpegInfo.MD5;
                                else
                                    md5 = utils.GetMd5(item.FullName);

                                if (jpegInfo != null)
                                {
                                    fileName = jpegInfo.FileName;

                                    try
                                    {
                                        dirPath = utils.CreateDirectoryPathFrom(jpegInfo.DateTime);
                                    }
                                    catch (Exception ex)
                                    {
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

                                targetPath = Path.Combine(dirPath, fileName);

                                break;
                            default:
                                log.PrintDebug(tag, "calculate MD5");
                                md5 = utils.GetMd5(item.FullName);
                                targetPath = utils.CreateDirectoryPathFrom(item.CreationTime);
                                targetPath = Path.Combine(targetPath, item.Name);
                                creationTime = item.CreationTime;
                                break;
                        }

                        bool canInsert = false;
                        bool canCopy = false;

                        if (!Images.ImageExists(targetPath, md5))
                        {
                            canInsert = true;
                        }
                        else
                        {
                            // duplicateList.Add(item.FullName);
                            log.PrintDebug(tag, item.FullName + " already in database.");
                            canInsert = false;
                        }

                        if (System.IO.File.Exists(Path.Combine(this.importDestinationPath, targetPath)) == false)
                        {
                            canCopy = true;
                        }
                        else
                        {
                            canCopy = false;
                            log.PrintDebug(tag, "file {0} already exists on target path {1}", item.Name, targetPath);
                        }

                        // transaction block
                        bool fileInserted = false;
                        bool fileCopied = false; ;

                        if (canCopy && canInsert)
                        {
                            try
                            {
                                // copy file
                                log.PrintDebug(tag, "{0} copy file to target path", item.Name);
                                var fullDestinationFilePath = Path.Combine(this.importDestinationPath, targetPath);
                                utils.CopyFile(item.FullName, fullDestinationFilePath);
                                log.PrintDebug(tag, "{0} copied to {1}", item.Name, targetPath);
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
                                    log.PrintDebug(tag, "{0} not in Database", item.Name);
                                    Images.Insert(targetPath, creationTime, md5);
                                    log.PrintDebug(tag, "{0} not in Database", item.Name);
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
                                InsertedImageCount++;
                        }
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
    }
}
