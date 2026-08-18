using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Data;
using WinFIMLog.FIM;

namespace WinFIMLog.Jobs
{
    internal sealed class FileSystemEnrichmentWorker : BackgroundService
    {
        private static readonly TimeSpan NormalizationWindow = TimeSpan.FromMilliseconds(75);
        private readonly FileSystemEventAttributionMonitor _attribution;
        private readonly FileSystemCaptureQueue _capture;
        private readonly ILiteDbContext _context;
        private readonly ILogger<FileSystemEnrichmentWorker> _logger;
        private readonly IBuffer<FileSystemChange> _output;
        private readonly Settings _settings;

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

        public override void Dispose()
        {
            _attribution.Dispose();
            base.Dispose();
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _attribution.Start();
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // A watcher notification means the namespace change occurred; it does not mean the
            // originating DELETE-capable handle has closed. Use a small, bounded window to fold
            // the accompanying Changed burst into the logical create/rename rather than waiting
            // (potentially forever) for the handle to close.
            while (await _capture.WaitToReadAsync())
            {
                var pending = new List<RawFileSystemNotification>();
                while (_capture.TryRead(out var raw))
                {
                    pending.Add(raw);
                }

                await Task.Delay(NormalizationWindow, CancellationToken.None);
                while (_capture.TryRead(out var raw))
                {
                    pending.Add(raw);
                }

                var succeeded = true;
                foreach (var raw in FileSystemNotificationWindow.Normalize(pending))
                {
                    try { await EnrichAsync(raw, CancellationToken.None); }
                    catch (Exception exception)
                    {
                        succeeded = false;
                        _logger.LogError(exception, "Could not enrich filesystem notification for {Path}", raw.FullPath);
                    }
                }
                for (var index = 0; index < pending.Count; index++) _capture.Complete(succeeded);
            }
        }

        private async Task EnrichAsync(RawFileSystemNotification raw, CancellationToken cancellationToken)
        {
            var configuration = _settings.Capture();
            // Retain the namespace observation even if a short-lived object has vanished before
            // enrichment. Basic watcher notifications cannot recover its metadata after the fact.
            var change = FileSystemChange.FromPath(raw.FullPath, raw.Category, configuration.HashLimitMB,
                configuration.ScopeHash, retainMissing: true);
            if (change == null)
            {
                return;
            }

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
                // A rename's previous projection lives under the old path. Looking up only the
                // destination would lose prior hash/ACL evidence and could suppress the rename
                // when the content itself did not change.
                previous = FileSystemChange.RetrievePreviousChange(raw.OldPath ?? raw.FullPath, _context);
                ApplyPreviousEvidence(change, previous);
            }

            // The normalization window already folds duplicate native notifications for one
            // logical operation. An admitted notification is integrity evidence even when its
            // final hash, size, and ACL match the latest-state projection.
            await _output.Add(change);
        }

        internal static void ApplyPreviousEvidence(FileSystemChange change, FileSystemChange? previous)
        {
            change.PreviousHash = previous?.CurrentHash ?? string.Empty;
            change.PreviousACL = previous?.ACLs ?? string.Empty;
            change.PreviousSizeBytes = previous?.CurrentSizeBytes;
            if (change.ChangeCategory == ChangeCategory.Deleted && previous is not null)
            {
                // Basic watcher removal notifications carry only the name. Recover directory
                // listing evidence from the projection built while the object still existed.
                change.ObjectType = previous.ObjectType;
            }
        }
    }
}
