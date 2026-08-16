using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    internal sealed class DurableEventOutboxSink(EventOutboxRepository outbox) : ILocalEventSink
    {
        public void Write(EventContract record, bool error = false) => outbox.Enqueue(record, error);
    }
}
