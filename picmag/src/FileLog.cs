using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace picmag
{
    public class FileLog : Log
    {
        private string _outputFilepath;
        public FileLog(string outputFilepath)
        {
            _outputFilepath = outputFilepath;
        }
        protected override void Print(string logLine)
        {
            base.Print(logLine);

            System.IO.File.AppendAllText(_outputFilepath, logLine + System.Environment.NewLine);
        }
    }
}