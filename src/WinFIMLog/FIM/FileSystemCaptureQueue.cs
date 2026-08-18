using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WinFIMLog.Health;

namespace WinFIMLog.FIM
{
    public sealed class FileSystemCaptureQueue
    {
        private readonly ConcurrentQueue<DateTimeOffset> _ages = new();
        private readonly Channel<RawFileSystemNotification> _channel;
        private readonly IHealthReporter _health;
        private readonly HealthMetrics _metrics;

        public FileSystemCaptureQueue(Settings settings, HealthMetrics metrics, IHealthReporter health)
            : this(settings.CaptureQueueCapacity, metrics, health) { }

        public FileSystemCaptureQueue(int capacity, HealthMetrics metrics, IHealthReporter health)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
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

        public void Complete(bool succeeded)
        {
            _ages.TryDequeue(out _);
            _metrics.SetOldest(_ages.TryPeek(out var oldest) ? oldest : null);
            if (succeeded)
            {
                _metrics.Completed();
            }
            else
            {
                _metrics.Failed();
            }
        }

        /// <summary>Stops admission after all watcher producers have stopped.</summary>
        public void CompleteWriter() => _channel.Writer.TryComplete();

        public IAsyncEnumerable<RawFileSystemNotification> ReadAllAsync() =>
            _channel.Reader.ReadAllAsync();

        public ValueTask<RawFileSystemNotification> ReadAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAsync(cancellationToken);

        internal bool TryRead(out RawFileSystemNotification notification) =>
            _channel.Reader.TryRead(out notification);

        internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
            _channel.Reader.WaitToReadAsync(cancellationToken);

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
    }
}
