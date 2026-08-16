using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    internal interface IEventRecordWriter
    {
        void Write(EventContract record, bool error = false);
    }
}
