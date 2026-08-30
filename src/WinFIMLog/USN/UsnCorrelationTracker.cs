using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinFIMLog.Data;
using WinFIMLog.FIM;

namespace WinFIMLog.USN
{
    /// <summary>
    /// Records which observations FileSystemWatcher already reported so the USN journal source can
    /// emit only what Tier 1 missed.
    /// </summary>
    /// <remarks>
    /// FileSystemWatcher events carry ETW process attribution and USN records carry none, so Tier 1
    /// always wins a duplicate. The journal source is therefore a gap filler: it publishes a record
    /// only when no watcher observation for the same normalized path and category was admitted
    /// inside the correlation window.
    ///
    /// The tracker only ever holds monitored-scope observations, because both sources filter to
    /// <see cref="EffectiveSettings.IsMonitoredPath"/> before reaching it. Its size is therefore
    /// bounded by in-scope activity rather than by total volume write activity. The capacity bound
    /// is a backstop: overflow drops the oldest entries, which can only cause a duplicate event,
    /// never a missed one.
    /// </remarks>
    internal sealed class UsnCorrelationTracker
    {
        private const int MaxEntries = 50_000;

        private readonly Dictionary<CorrelationKey, DateTimeOffset> observations = new();
        private readonly Lock gate = new();
        private readonly TimeSpan window;
        private long overflowDrops;
        private long suppressedUsnRecords;
        private long admittedUsnRecords;

        public UsnCorrelationTracker(TimeSpan window)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
            this.window = window;
        }

        /// <summary>USN records suppressed because FileSystemWatcher already reported them.</summary>
        public long SuppressedUsnRecords => Interlocked.Read(ref suppressedUsnRecords);

        /// <summary>USN records published because no watcher observation matched them.</summary>
        public long AdmittedUsnRecords => Interlocked.Read(ref admittedUsnRecords);

        /// <summary>Entries discarded because the capacity backstop was reached.</summary>
        public long OverflowDrops => Interlocked.Read(ref overflowDrops);

        internal int Count
        {
            get { lock (gate) { return observations.Count; } }
        }

        /// <summary>Records that FileSystemWatcher produced an observation for this path.</summary>
        public void RecordWatcherObservation(string fullPath, ChangeCategory category, DateTimeOffset observedAt)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            var key = new CorrelationKey(LiteDbContext.NormalizeEntity(fullPath), category);
            lock (gate)
            {
                if (observations.Count >= MaxEntries && !observations.ContainsKey(key))
                {
                    EvictOldestUnlocked();
                }

                // A repeated observation extends the window rather than starting a second entry.
                observations[key] = observedAt;
            }
        }

        /// <summary>
        /// Decides whether a USN record should be published, and counts the outcome.
        /// </summary>
        /// <param name="fullPath">Resolved path of the USN record.</param>
        /// <param name="category">Category the record's reason flags mapped to.</param>
        /// <param name="recordedAt">Timestamp carried by the USN record itself.</param>
        /// <returns>True when Tier 1 did not report this observation and the record should publish.</returns>
        public bool ShouldPublish(string fullPath, ChangeCategory category, DateTimeOffset recordedAt)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                Interlocked.Increment(ref admittedUsnRecords);
                return true;
            }

            var key = new CorrelationKey(LiteDbContext.NormalizeEntity(fullPath), category);
            lock (gate)
            {
                if (observations.TryGetValue(key, out var watcherObservedAt) &&
                    IsWithinWindow(watcherObservedAt, recordedAt))
                {
                    Interlocked.Increment(ref suppressedUsnRecords);
                    return false;
                }
            }

            Interlocked.Increment(ref admittedUsnRecords);
            return true;
        }

        /// <summary>Removes observations that can no longer suppress an incoming USN record.</summary>
        public void Prune(DateTimeOffset now)
        {
            lock (gate)
            {
                if (observations.Count == 0)
                {
                    return;
                }

                var expired = observations
                    .Where(entry => now - entry.Value > window)
                    .Select(entry => entry.Key)
                    .ToArray();

                foreach (var key in expired)
                {
                    observations.Remove(key);
                }
            }
        }

        /// <summary>A watcher observation suppresses a record recorded near it in either direction.</summary>
        /// <remarks>
        /// The absolute difference is used rather than a one-sided comparison because journal
        /// timestamps and watcher capture times come from different clocks in the same operation
        /// and their ordering is not guaranteed.
        /// </remarks>
        private bool IsWithinWindow(DateTimeOffset watcherObservedAt, DateTimeOffset recordedAt)
        {
            var difference = watcherObservedAt - recordedAt;
            if (difference < TimeSpan.Zero)
            {
                difference = difference.Negate();
            }

            return difference <= window;
        }

        private void EvictOldestUnlocked()
        {
            var oldest = default(CorrelationKey);
            var oldestAt = DateTimeOffset.MaxValue;
            var found = false;

            foreach (var entry in observations)
            {
                if (entry.Value < oldestAt)
                {
                    oldestAt = entry.Value;
                    oldest = entry.Key;
                    found = true;
                }
            }

            if (found)
            {
                observations.Remove(oldest);
                Interlocked.Increment(ref overflowDrops);
            }
        }

        private readonly record struct CorrelationKey(string NormalizedPath, ChangeCategory Category);
    }
}
