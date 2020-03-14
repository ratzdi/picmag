
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace picmag
{
    public class Log : ILog
    {
        public Log()
        {
        }
        public virtual IFormatProvider FormatProvider
        {
            get
            {
                return Thread.CurrentThread.CurrentCulture;
            }
        }
        protected virtual string FormatLogLine(String tag, String level, String msg)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendFormat(this.FormatProvider, "[{0}][{1,4}][{2}]: {3}", DateTime.Now.ToString("ddMMyy-HHmmss.ffff"),
                tag.Length >= 4 ? tag.Substring(0, 4) : tag, level, msg);
            return stringBuilder.ToString();
        }
        protected virtual void Print(String logLine)
        {
            Console.WriteLine(logLine);
        }
        public void PrintDebug(String tag, String format, params Object[] args)
        {
            var logLine = FormatLogLine(tag, "D", String.Format(FormatProvider, format, args));
            Print(logLine);
        }
        public void PrintError(String tag, String format, params Object[] args)
        {
            var logLine = FormatLogLine(tag, "E", String.Format(FormatProvider, format, args));
            Print(logLine);
        }

        public void PrintInfo(String tag, String format, params Object[] args)
        {
            var logLine = FormatLogLine(tag, "I", String.Format(FormatProvider, format, args));
            Print(logLine);
        }
    }
}