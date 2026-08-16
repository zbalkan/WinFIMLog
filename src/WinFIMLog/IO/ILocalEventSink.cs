namespace WinFIMLog.IO
{
    public interface ILocalEventSink
    {
        void Write(ushort eventId, string message, bool error = false);
    }
}
