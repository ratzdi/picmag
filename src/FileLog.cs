using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace picmag
{
    public class FileLog : Log
    {
        private string outputFilepath;
        public FileLog(string outputFilepath)
        {
            this.outputFilepath = outputFilepath;
        }
        protected override void Print(string logLine)
        {
            base.Print(logLine);
            System.IO.File.AppendAllTextAsync(outputFilepath, logLine + System.Environment.NewLine);
            System.Diagnostics.Debug.WriteLine(logLine);
        }
    }
}