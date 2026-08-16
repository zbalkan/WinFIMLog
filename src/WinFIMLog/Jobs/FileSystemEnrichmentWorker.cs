using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.Health;

namespace WinFIMLog.Jobs
{
    internal sealed class FileSystemEnrichmentWorker : BackgroundService
    {
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);
        private readonly ConcurrentDictionary<string, (string Fingerprint, DateTime ExpiresUtc)> _recentChanges = new();
        private readonly FileSystemCaptureQueue _capture;
        private readonly ILiteDbContext _context;
        private readonly IBuffer<FileSystemChange> _output;
        private readonly FileSystemEventAttributionMonitor _attribution;
        private readonly Settings _settings;
        private readonly ILogger<FileSystemEnrichmentWorker> _logger;

        public FileSystemEnrichmentWorker(FileSystemCaptureQueue capture, ILiteDbContext context,
            IBuffer<FileSystemChange> output, Settings settings, ILogger<FileSystemEnrichmentWorker> logger)
        {
            _capture = capture;
            _context = context;
            _output = output;
            _settings = settings;
            _logger = logger;
            _attribution = new FileSystemEventAttributionMonitor(logger, settings);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _attribution.Start();
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Cancellation stops producers first. Channel completion, not cancellation,
            // is the acknowledgement that every admitted raw item has been observed.
            await foreach (var raw in _capture.ReadAllAsync())
            {
                var succeeded = false;
                try
                {
                    await EnrichAsync(raw, CancellationToken.None);
                    succeeded = true;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not enrich filesystem notification for {Path}", raw.FullPath);
                }
                finally { _capture.Complete(succeeded); }
            }
        }

        private async Task EnrichAsync(RawFileSystemNotification raw, CancellationToken cancellationToken)
        {
            var configuration = _settings.Capture();
            var change = FileSystemChange.FromPath(raw.FullPath, raw.Category, configuration.HashLimitMB, configuration.ScopeHash);
            if (change == null) return;
            change.OldPath = raw.OldPath;
            change.NewPath = raw.NewPath;

            // Correlation waiting, hashing, ACL access and database reads all happen here, never
            // on the native watcher callback thread.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (_attribution.TryGet(raw.FullPath, out var attribution))
                {
                    change.ProcessID = attribution.ProcessID;
                    change.ProcessName = attribution.ProcessName;
                    change.Username = attribution.Username;
                    change.UserSID = attribution.UserSID;
                    change.ProcessSequenceNumber = attribution.ProcessSequenceNumber;
                    change.AttributionStatus = attribution.Status;
                    change.AttributionMethod = "KernelETWProcessSequence";
                    change.AttributionConfidence = attribution.Status == AttributionStatus.Attributed ? "High" : "None";
                    change.AttributionSourceTimestamp = attribution.SourceTimestamp;
                    change.AttributionMissingReason = attribution.MissingReason;
                    break;
                }
                await Task.Delay(10, cancellationToken);
            }

            if (change.AttributionStatus == AttributionStatus.Unattributed)
            {
                change.AttributionMethod = "KernelETWProcessSequence";
                change.AttributionConfidence = "None";
                change.AttributionMissingReason = "NoCorrelatedFileEvent";
            }

            FileSystemChange? previous = null;
            if (configuration.EnableLocalDatabase)
            {
                previous = FileSystemChange.RetrievePreviousChange(raw.FullPath, _context);
                change.PreviousHash = previous?.CurrentHash ?? string.Empty;
            }

            var fingerprint = $"{change.ChangeCategory}\0{change.ObjectType}\0{change.CurrentHash}\0{change.ACLs}";
            var now = DateTime.UtcNow;
            var duplicate = _recentChanges.TryGetValue(change.FullPath, out var cached) &&
                cached.ExpiresUtc > now && cached.Fingerprint == fingerprint;
            if (cached.ExpiresUtc <= now)
                _recentChanges.TryRemove(change.FullPath, out _);
            var changed = change.ChangeCategory is ChangeCategory.Created or ChangeCategory.Deleted || previous == null ||
                change.ObjectType != previous.ObjectType ||
                !string.Equals(change.CurrentHash, previous.CurrentHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(change.ACLs, previous.ACLs, StringComparison.Ordinal);
            if (changed && !duplicate)
            {
                await _output.Add(change);
                _recentChanges[raw.FullPath] = (fingerprint, now + DuplicateWindow);
            }
        }

        public override void Dispose()
        {
            _attribution.Dispose();
            base.Dispose();
        }
    }
}
