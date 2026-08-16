using System.Diagnostics;

namespace WinFIMLog.IO
{
    internal sealed class WindowsEventLogSink : ILocalEventSink
    {
        private const string Source = "WinFIMLog";

        public void Write(ushort eventId, string message, bool error = false) =>
            EventLog.WriteEntry(Source, message,
                error ? EventLogEntryType.Error : EventLogEntryType.Information, eventId);
    }
}
