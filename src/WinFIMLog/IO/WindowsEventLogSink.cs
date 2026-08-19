using System;
using System.Diagnostics;
using System.Threading;
using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    internal sealed class WindowsEventLogSink : IEventRecordWriter
    {
        private readonly Action<string, string> createSource;
        private readonly Func<string, string?> logNameFromSource;
        private readonly Lock sourceLock = new();
        private readonly Func<string, bool> sourceExists;
        private readonly Action<string, string, EventLogEntryType, int> writeEntry;

        public WindowsEventLogSink() : this(EventLog.WriteEntry, EventLog.SourceExists,
            source => EventLog.LogNameFromSourceName(source, "."),
            (source, logName) => EventLog.CreateEventSource(new EventSourceCreationData(source, logName)))
        { }

        internal WindowsEventLogSink(Action<string, string, EventLogEntryType, int> writeEntry) :
            this(writeEntry, _ => true, _ => null, (_, _) => { })
        { }

        internal WindowsEventLogSink(Action<string, string, EventLogEntryType, int> writeEntry,
            Func<string, bool> sourceExists, Func<string, string?> logNameFromSource,
            Action<string, string> createSource)
        {
            this.writeEntry = writeEntry;
            this.sourceExists = sourceExists;
            this.logNameFromSource = logNameFromSource;
            this.createSource = createSource;
        }

        public void Write(EventContract record, bool error = false)
        {
            EventIdCatalog.Validate(record.EventId, record.RecordType);
            var (source, logName) = Source();
            EnsureSource(source, logName);
            writeEntry(source, record.FormatEventLogMessage(),
                EventIdCatalog.EntryType(record.EventId, error), record.EventId);
        }

        private void EnsureSource(string source, string logName)
        {
            if (sourceExists(source))
            {
                EnsureCorrectLog(source, logName);
                return;
            }

            lock (sourceLock)
            {
                if (!sourceExists(source))
                {
                    createSource(source, logName);
                }

                EnsureCorrectLog(source, logName);
            }
        }

        private void EnsureCorrectLog(string source, string expectedLogName)
        {
            var registeredLogName = logNameFromSource(source);
            if (!string.IsNullOrWhiteSpace(registeredLogName) &&
                !string.Equals(registeredLogName, expectedLogName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Event Log source '{source}' is registered to '{registeredLogName}', not '{expectedLogName}'.");
            }
        }

        private static (string Source, string LogName) Source() =>
            ("WinFIMLog", "WinFIMLog");
    }
}
