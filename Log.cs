
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
        private void Print(String tag, String level, String msg)
        {
            Console.WriteLine("[{0}][{1,4}][{2}]: {3}", DateTime.Now.ToString("ddMMyy-HHmmss.ffff"),
                tag.Length >= 4 ? tag.Substring(0, 4) : tag, level, msg);
        }
        public void PrintDebug(String tag, String format, params Object[] args)
        {
            Print(tag, "D", String.Format(FormatProvider, format, args));
        }
        public void PrintError(String tag, String format, params Object[] args)
        {
            Print(tag, "E", String.Format(FormatProvider, format, args));
        }

        public void PrintInfo(String tag, String format, params Object[] args)
        {
            Print(tag, "I", String.Format(FormatProvider, format, args));
        }
    }
}