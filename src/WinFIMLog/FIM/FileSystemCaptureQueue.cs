using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WinFIMLog.Health;

namespace WinFIMLog.FIM
{
    public sealed class FileSystemCaptureQueue
    {
        private readonly Channel<RawFileSystemNotification> _channel;
        private readonly ConcurrentQueue<DateTimeOffset> _ages = new();
        private readonly HealthMetrics _metrics;
        private readonly IHealthReporter _health;

        public FileSystemCaptureQueue(Settings settings, HealthMetrics metrics, IHealthReporter health)
            : this(settings.CaptureQueueCapacity, metrics, health) { }

        public FileSystemCaptureQueue(int capacity, HealthMetrics metrics, IHealthReporter health)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _metrics = metrics;
            _health = health;
            _channel = Channel.CreateBounded<RawFileSystemNotification>(new BoundedChannelOptions(capacity)
            {
                // Wait mode makes TryWrite return false rather than silently discarding a write.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public bool TryAdmit(RawFileSystemNotification notification)
        {
            if (_channel.Writer.TryWrite(notification))
            {
                _ages.Enqueue(notification.CapturedAt);
                _metrics.Admitted(notification.CapturedAt);
                return true;
            }

            _metrics.DroppedItem();
            _health.CoverageGap("FileSystemWatcher", notification.Scope, "CaptureQueueFull");
            return false;
        }

        public async ValueTask<RawFileSystemNotification> ReadAsync(CancellationToken cancellationToken) =>
            await _channel.Reader.ReadAsync(cancellationToken);

        public void Complete(bool succeeded)
        {
            _ages.TryDequeue(out _);
            _metrics.SetOldest(_ages.TryPeek(out var oldest) ? oldest : null);
            if (succeeded) _metrics.Completed(); else _metrics.Failed();
        }
    }
}
