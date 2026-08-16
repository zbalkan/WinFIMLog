using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.IO;
using WinFIMLog.Health;

namespace WinFIMLog.Snapshots
{
    /// <summary>Serialises periodic and recovery-triggered Tier 0 scans.</summary>
    public sealed class SnapshotService : BackgroundService, ISnapshotCoordinator
    {
        private readonly BaselineRepository repository;
        private readonly Settings settings;
        private readonly ILogger<SnapshotService> logger;
        private readonly ILocalEventSink eventSink;
        private readonly IHealthReporter health;
        private readonly Channel<SnapshotRequest> requests = Channel.CreateUnbounded<SnapshotRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        public SnapshotService(BaselineRepository repository, Settings settings, ILogger<SnapshotService> logger,
            ILocalEventSink eventSink, IHealthReporter health)
        { this.repository = repository; this.settings = settings; this.logger = logger; this.eventSink = eventSink; this.health = health; }

        public void RequestFileSystemSnapshot(string reason, string? affectedScope = null) =>
            requests.Writer.TryWrite(new SnapshotRequest(BaselineSource.FileSystem, reason, affectedScope));

        public void RequestRegistrySnapshot(string reason, string? affectedScope = null) =>
            requests.Writer.TryWrite(new SnapshotRequest(BaselineSource.Registry, reason, affectedScope));

        public void RequestScopeSnapshot(string reason)
        {
            RequestFileSystemSnapshot(reason);
            if (settings.EnableRegistryMonitoring) RequestRegistrySnapshot(reason);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var fileSystemDue = DateTimeOffset.MinValue;
            var registryDue = DateTimeOffset.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                PublishPendingFindings();
                var now = DateTimeOffset.UtcNow;
                if (now >= fileSystemDue)
                {
                    await RunFileSystemSnapshot(stoppingToken);
                    fileSystemDue = DateTimeOffset.UtcNow.AddSeconds(settings.FileSystemSnapshotInterval);
                }
                if (settings.EnableRegistryMonitoring && now >= registryDue)
                {
                    await RunRegistrySnapshot(stoppingToken);
                    registryDue = DateTimeOffset.UtcNow.AddSeconds(settings.RegistrySnapshotInterval);
                }

                var nextDue = settings.EnableRegistryMonitoring && registryDue < fileSystemDue ? registryDue : fileSystemDue;
                var remaining = nextDue - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.FromSeconds(5)) remaining = TimeSpan.FromSeconds(5);
                var delay = Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, stoppingToken);
                var available = requests.Reader.WaitToReadAsync(stoppingToken).AsTask();
                if (await Task.WhenAny(delay, available) == delay) continue;

                var runFileSystem = false;
                var runRegistry = false;
                while (requests.Reader.TryRead(out var request))
                {
                    logger.LogWarning("Tier 0 {Source} snapshot requested: {Reason}; affected scope {AffectedScope}",
                        request.Source, request.Reason, request.AffectedScope ?? "all configured scope");
                    runFileSystem |= request.Source == BaselineSource.FileSystem;
                    runRegistry |= request.Source == BaselineSource.Registry;
                }
                if (runFileSystem)
                {
                    await RunFileSystemSnapshot(stoppingToken);
                    fileSystemDue = DateTimeOffset.UtcNow.AddSeconds(settings.FileSystemSnapshotInterval);
                }
                if (runRegistry && settings.EnableRegistryMonitoring)
                {
                    await RunRegistrySnapshot(stoppingToken);
                    registryDue = DateTimeOffset.UtcNow.AddSeconds(settings.RegistrySnapshotInterval);
                }
            }
        }

        internal async Task RunRegistrySnapshot(CancellationToken cancellationToken)
        {
            var baseline = repository.Begin(BaselineSource.Registry, settings.ScopeHash,
                SourceIdentityProvider.Registry(settings.MonitoredKeys), algorithmVersion: "registry-v1");
            try
            {
                var members = await Task.Run(() => new RegistrySnapshotSource().Capture(settings.MonitoredKeys), cancellationToken);
                var results = repository.ReconcileAndComplete(baseline, members);
                EmitFindings(baseline, results);
                logger.LogInformation("Completed registry baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}", baseline.Id, baseline.ItemCount, baseline.ScopeHash);
            }
            catch (OperationCanceledException) { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Registry baseline {BaselineId} failed", baseline.Id); }
        }

        internal async Task RunFileSystemSnapshot(CancellationToken cancellationToken)
        {
            var baseline = repository.Begin(BaselineSource.FileSystem, settings.ScopeHash,
                SourceIdentityProvider.FileSystem(settings.MonitoredPaths));
            try
            {
                var source = new FileSystemSnapshotSource(settings.HashLimitMB);
                _ = await Task.Run(() => source.Capture(settings.MonitoredPaths), cancellationToken);
                baseline.Status = BaselineStatus.Reconciling;
                var second = await Task.Run(() => source.Capture(settings.MonitoredPaths), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var results = repository.ReconcileAndCompleteAfterSecondPass(baseline, Array.Empty<BaselineMember>(), second);
                EmitFindings(baseline, results);
                logger.LogInformation("Completed filesystem baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}",
                    baseline.Id, baseline.ItemCount, baseline.ScopeHash);
            }
            catch (OperationCanceledException) { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Filesystem baseline {BaselineId} failed", baseline.Id); }
        }

        private void EmitFindings(BaselineMetadata baseline, System.Collections.Generic.IEnumerable<ReconciliationResult> results)
        {
            foreach (var result in results)
                TryPublishFinding(baseline, result);
        }

        private void PublishPendingFindings()
        {
            foreach (var result in repository.PendingResults())
            {
                var baseline = repository.Find(result.BaselineId);
                if (baseline is not null) TryPublishFinding(baseline, result);
            }
        }

        private void TryPublishFinding(BaselineMetadata baseline, ReconciliationResult result)
        {
            try
            {
                WriteFindingWithRetry($"BaselineId={baseline.Id} Source={baseline.Source} ScopeHash={baseline.ScopeHash} Change={result.Change} Identity={result.Identity} OldPath={result.OldPath} NewPath={result.NewPath} DetectedAt={result.DetectedAt:O}");
                repository.RecordDeliveryAttempt(result, true);
            }
            catch (Exception exception)
            {
                repository.RecordDeliveryAttempt(result, false);
                logger.LogError(exception, "Baseline finding {FindingId} remains in the durable local outbox", result.Id);
            }
        }

        private void WriteFindingWithRetry(string message)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try { eventSink.Write(7795, message); return; }
                catch (Exception exception)
                {
                    last = exception;
                    health.SinkFailure("EventLog", exception.GetType().Name, attempt);
                    if (attempt < 3) Thread.Sleep(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))));
                }
            }
            throw new InvalidOperationException("Event Log baseline finding write failed after retries.", last);
        }

        private sealed record SnapshotRequest(BaselineSource Source, string Reason, string? AffectedScope);
    }
}
