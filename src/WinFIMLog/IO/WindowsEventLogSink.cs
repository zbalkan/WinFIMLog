using System;
using System.Diagnostics;
using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    internal sealed class WindowsEventLogSink : IEventRecordWriter
    {
        private readonly Action<string, string, EventLogEntryType, int> writeEntry;

        public WindowsEventLogSink() : this(EventLog.WriteEntry)
        { }

        internal WindowsEventLogSink(Action<string, string, EventLogEntryType, int> writeEntry) =>
            this.writeEntry = writeEntry;

        public void Write(EventContract record, bool error = false)
        {
            EventIdCatalog.Validate(record.EventId, record.RecordType, record.Channel);
            var source = record.Channel switch
            {
                EventChannel.Baseline => "WinFIM-Baseline",
                EventChannel.Diagnostic => "WinFIM-Diagnostic",
                _ => "WinFIM-Operational"
            };
            writeEntry(source, record.FormatEventLogMessage(),
                EventIdCatalog.EntryType(record.EventId, error), record.EventId);
        }
    }
}
