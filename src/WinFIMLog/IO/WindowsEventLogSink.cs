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
                EventChannel.Baseline => "WinFIM-Baseline",
                EventChannel.Diagnostic => "WinFIM-Diagnostic",
                _ => "WinFIM-Operational"
            };
            EventLog.WriteEntry(source, record.FormatEventLogMessage(),
                error ? EventLogEntryType.Error : EventLogEntryType.Information, record.EventId);
        }
    }
}
