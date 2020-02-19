using System;
using System.IO;

namespace picmag
{
    public class FileFoundEventArgs : EventArgs
    {
        public FileFoundEventArgs(FileInfo fileInfo)
        {
            FileInfo = fileInfo;
        }
        public FileInfo FileInfo { get; private set; }
    }
}