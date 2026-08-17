using System;
using System.Threading;

namespace WinFIMLog.Snapshots
{
    public sealed class SnapshotHealthState
    {
        private long fileSystemDurationTicks;
        private int fileSystemFailures;
        private long fileSystemLastSuccess;
        private int fileSystemRunning;
        private long fileSystemStarted;
        private long registryDurationTicks;
        private int registryFailures;
        private long registryLastSuccess;
        private int registryRunning;
        private long registryStarted;
        public int FileSystemFailures => Volatile.Read(ref fileSystemFailures);
        public TimeSpan FileSystemLastDuration => TimeSpan.FromTicks(Interlocked.Read(ref fileSystemDurationTicks));
        public DateTimeOffset? FileSystemLastSuccess => ReadTime(ref fileSystemLastSuccess);
        public bool FileSystemRunning => Volatile.Read(ref fileSystemRunning) != 0;
        public DateTimeOffset? FileSystemStarted => ReadTime(ref fileSystemStarted);
        public int RegistryFailures => Volatile.Read(ref registryFailures);
        public TimeSpan RegistryLastDuration => TimeSpan.FromTicks(Interlocked.Read(ref registryDurationTicks));
        public DateTimeOffset? RegistryLastSuccess => ReadTime(ref registryLastSuccess);
        public bool RegistryRunning => Volatile.Read(ref registryRunning) != 0;
        public DateTimeOffset? RegistryStarted => ReadTime(ref registryStarted);

        internal void Failed(BaselineSource source, int count)
        {
            Volatile.Write(ref Failures(source), count);
            Volatile.Write(ref Running(source), 0);
        }

        internal void Started(BaselineSource source)
        {
            Interlocked.Exchange(ref StartedAt(source), DateTimeOffset.UtcNow.UtcTicks);
            Volatile.Write(ref Running(source), 1);
        }

        internal void Succeeded(BaselineSource source)
        {
            var now = DateTimeOffset.UtcNow;
            var started = Interlocked.Read(ref StartedAt(source));
            Interlocked.Exchange(ref Duration(source), started == 0 ? 0 : now.UtcTicks - started);
            Interlocked.Exchange(ref LastSuccess(source), now.UtcTicks);
            Volatile.Write(ref Failures(source), 0);
            Volatile.Write(ref Running(source), 0);
        }

        private static DateTimeOffset? ReadTime(ref long value)
        { var ticks = Interlocked.Read(ref value); return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero); }

        private ref long Duration(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemDurationTicks; return ref registryDurationTicks; }

        private ref int Failures(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemFailures; return ref registryFailures; }

        private ref long LastSuccess(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemLastSuccess; return ref registryLastSuccess; }

        private ref int Running(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemRunning; return ref registryRunning; }

        private ref long StartedAt(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemStarted; return ref registryStarted; }
    }
}
