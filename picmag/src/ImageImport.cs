using System;
using System.IO;
using System.Threading;

namespace picmag
{
    public class ImageImport
    {
        #region Events
        public event EventHandler<FileFoundEventArgs> AddFile;
        #endregion
        public uint TotalFilesCount { get; private set; }
        private CancellationTokenSource cancellationTokenSource;
        private string rootPath;
        public ImageImport(CancellationTokenSource cancelationTokenSource, string rootPath)
        {
            cancellationTokenSource = cancelationTokenSource;
            this.rootPath = rootPath;
        }
        public void Start()
        {
            Start(rootPath);
        }
        private void Start(string rootPath)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            var d = new DirectoryInfo(rootPath);
            var files = d.GetFiles();
            foreach (var file in files)
            {
                AddFile(this, new FileFoundEventArgs(file));
                TotalFilesCount++;
            }

            var directories = d.GetDirectories("*");

            if (directories.Length > 0)
            {
                foreach (var dir in directories)
                    Start(dir.FullName);
            }
        }
    }
}