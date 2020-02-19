
using System;

namespace picmag
{
    public interface ILog
    {
        void PrintInfo(String tag, String format, params Object[] msg);
        void PrintError(String tag, String format, params Object[] msg);
        void PrintDebug(String tag, String format, params Object[] msg);
    }
}