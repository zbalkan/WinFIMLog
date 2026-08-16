using System.Diagnostics;
using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    internal sealed class WindowsEventLogSink : IEventRecordWriter
    {
        public void Write(EventContract record, bool error = false)
        {
            var source = record.Channel switch
            {
                EventChannel.Baseline => "WinFIMLog-Baseline",
                EventChannel.Diagnostic => "WinFIMLog-Diagnostic",
                _ => "WinFIMLog-Operational"
            };
            EventLog.WriteEntry(source, record.ToJson(),
                error ? EventLogEntryType.Error : EventLogEntryType.Information, record.EventId);
        }
    }
}
