using System;
using System.Threading;

namespace WinFIMLog.Health
{
    public sealed class HealthMetrics
    {
        private long _accepted;
        private long _depth;
        private long _dropped;
        private long _enrichmentFailures;
        private long _oldestTicks;
        private long _processed;
        public long Accepted => Interlocked.Read(ref _accepted);
        public long Dropped => Interlocked.Read(ref _dropped);
        public long EnrichmentFailures => Interlocked.Read(ref _enrichmentFailures);
        public TimeSpan OldestItemAge => _oldestTicks == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - new DateTimeOffset(Interlocked.Read(ref _oldestTicks), TimeSpan.Zero);
        public long Processed => Interlocked.Read(ref _processed);
        public long QueueDepth => Interlocked.Read(ref _depth);

        internal void Admitted(DateTimeOffset capturedAt)
        {
            Interlocked.Increment(ref _accepted);
            Interlocked.Increment(ref _depth);
            Interlocked.CompareExchange(ref _oldestTicks, capturedAt.UtcTicks, 0);
        }

        internal void Completed()
        { Interlocked.Increment(ref _processed); ItemRemoved(); }

        internal void DroppedItem() => Interlocked.Increment(ref _dropped);

        internal void Failed()
        { Interlocked.Increment(ref _enrichmentFailures); ItemRemoved(); }

        internal void SetOldest(DateTimeOffset? value) => Interlocked.Exchange(ref _oldestTicks, value?.UtcTicks ?? 0);

        private void ItemRemoved() => Interlocked.Decrement(ref _depth);
    }
}
