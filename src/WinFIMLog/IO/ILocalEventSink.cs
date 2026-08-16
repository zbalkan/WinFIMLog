namespace WinFIMLog.IO
{
    public interface ILocalEventSink
    {
        void Write(Events.EventContract record, bool error = false);
    }
}
