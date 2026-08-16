using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WinFIMLog.Events;

namespace WinFIMLog.IO
{
    /// <summary>Bounds repeated finding bursts while preserving a structured summary.</summary>
    internal sealed class BurstAggregatingEventSink : BackgroundService, ILocalEventSink
    {
        private readonly IEventRecordWriter inner;
        private readonly BurstAggregationOptions options;
        private readonly ConcurrentDictionary<string, Bucket> buckets = new();

        public BurstAggregatingEventSink(IEventRecordWriter inner, IOptions<BurstAggregationOptions> options)
        { this.inner = inner; this.options = options.Value; }

        public void Write(EventContract record, bool error = false)
        {
            if (!options.Enabled || options.Threshold < 1 || record.RecordType is not ("FileSystemFinding" or "RegistryFinding"))
            { inner.Write(record, error); return; }

            var key = $"{record.EventId}:{record.RecordType}:{record.ScopeHash}";
            var bucket = buckets.GetOrAdd(key, _ => new Bucket(record, DateTimeOffset.UtcNow));
            var count = Interlocked.Increment(ref bucket.Count);
            bucket.Last = record;
            if (count <= options.Threshold) inner.Write(record, error);
        }

        internal void Flush(DateTimeOffset now)
        {
            foreach (var pair in buckets)
            {
                var bucket = pair.Value;
                if (now - bucket.StartedAt < TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)) ||
                    !buckets.TryRemove(pair.Key, out bucket)) continue;
                if (bucket.Count <= options.Threshold) continue;
                inner.Write(EventContract.Create(7796, "Aggregation", Guid.NewGuid().ToString("N"),
                    bucket.First.ScopeHash, new Dictionary<string, object?>
                    {
                        ["sourceEventId"] = bucket.First.EventId,
                        ["groupKey"] = pair.Key,
                        ["count"] = bucket.Count - options.Threshold,
                        ["windowStartedAt"] = bucket.StartedAt,
                        ["windowEndedAt"] = now,
                        ["sampleRecordId"] = bucket.Last.RecordId
                    }));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)), stoppingToken);
                Flush(DateTimeOffset.UtcNow);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        { Flush(DateTimeOffset.MaxValue); return base.StopAsync(cancellationToken); }

        private sealed class Bucket(EventContract first, DateTimeOffset startedAt)
        {
            internal readonly EventContract First = first;
            internal readonly DateTimeOffset StartedAt = startedAt;
            internal long Count;
            internal EventContract Last = first;
        }
    }
}
