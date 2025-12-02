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