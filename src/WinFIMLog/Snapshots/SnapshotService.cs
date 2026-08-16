using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinFIMLog.Data;
using WinFIMLog.Health;

namespace WinFIMLog.Snapshots
{
    /// <summary>Coordinates independent, bounded Tier 0 source schedulers.</summary>
    public sealed class SnapshotService : BackgroundService, ISnapshotCoordinator
    {
        private readonly BaselineRepository repository;
        private readonly Settings settings;
        private readonly ILogger<SnapshotService> logger;
        private readonly IHealthReporter health;
        private readonly SnapshotHealthState state;
        private readonly RetentionOptions retention;
        private readonly Channel<SnapshotRequest> fileSystemRequests = BoundedRequests();
        private readonly Channel<SnapshotRequest> registryRequests = BoundedRequests();

        internal int PendingFileSystemRequests => fileSystemRequests.Reader.Count;
        internal int PendingRegistryRequests => registryRequests.Reader.Count;

        public SnapshotService(BaselineRepository repository, Settings settings,
            ILogger<SnapshotService> logger, IHealthReporter health, SnapshotHealthState state,
            IOptions<RetentionOptions> retention)
        {
            this.repository = repository;
            this.settings = settings;
            this.logger = logger;
            this.health = health;
            this.state = state;
            this.retention = retention.Value;
        }

        public void RequestFileSystemSnapshot(string reason, string? affectedScope = null) =>
            fileSystemRequests.Writer.TryWrite(new SnapshotRequest(reason, affectedScope));

        public void RequestRegistrySnapshot(string reason, string? affectedScope = null) =>
            registryRequests.Writer.TryWrite(new SnapshotRequest(reason, affectedScope));

        public void RequestScopeSnapshot(string reason)
        {
            RequestFileSystemSnapshot(reason);
            if (settings.EnableRegistryMonitoring) RequestRegistrySnapshot(reason);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
            RunSourceLoop(BaselineSource.FileSystem, fileSystemRequests.Reader, stoppingToken),
            RunSourceLoop(BaselineSource.Registry, registryRequests.Reader, stoppingToken));

        private async Task RunSourceLoop(BaselineSource source, ChannelReader<SnapshotRequest> requests,
            CancellationToken cancellationToken)
        {
            var due = DateTimeOffset.MinValue;
            var failures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var configuration = settings.Capture();
                if (source == BaselineSource.Registry && !configuration.EnableRegistryMonitoring)
                {
                    await WaitForRequestOrDelay(requests, TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (now < due)
                {
                    var request = await WaitForRequestOrDelay(requests, due - now, cancellationToken);
                    if (request is null) continue;
                    logger.LogWarning(
                        "Tier 0 {Source} full snapshot requested: {Reason}; affected scope {AffectedScope} promoted to full configured scope",
                        source, request.Reason, request.AffectedScope ?? "all configured scope");
                    DrainRequests(requests);
                }

                state.Started(source);
                var succeeded = source == BaselineSource.FileSystem
                    ? await RunFileSystemSnapshot(cancellationToken)
                    : await RunRegistrySnapshot(cancellationToken);
                if (succeeded)
                {
                    state.Succeeded(source);
                    if (failures > 0)
                        health.SourceRecovered($"{source}Snapshot", configuration.ScopeHash,
                            $"CompletedAfter{failures}Failures");
                    failures = 0;
                    var interval = source == BaselineSource.FileSystem
                        ? configuration.FileSystemSnapshotInterval : configuration.RegistrySnapshotInterval;
                    due = DateTimeOffset.UtcNow.AddSeconds(interval);
                }
                else
                {
                    failures++;
                    state.Failed(source, failures);
                    health.CoverageGap($"{source}Snapshot", configuration.ScopeHash,
                        $"ScanFailed;RetryAttempt={failures}", 0);
                    due = DateTimeOffset.UtcNow.Add(RetryDelay(failures));
                }
            }
        }

        internal async Task<bool> RunRegistrySnapshot(CancellationToken cancellationToken)
        {
            var configuration = settings.Capture();
            IReadOnlyList<string> resolvedRoots;
            try { resolvedRoots = RegistrySnapshotSource.ResolveRoots(configuration.MonitoredKeys); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not resolve the active-user registry hive manifest");
                return false;
            }
            var baseline = repository.Begin(BaselineSource.Registry, configuration.ScopeHash,
                SourceIdentityProvider.RegistryResolved(resolvedRoots), algorithmVersion: "registry-v1");
            baseline.ConsistencyMethod = "ResolvedLoadedHiveManifest";
            baseline.ObservationPasses = 1;
            try
            {
                var members = await Task.Run(() => new RegistrySnapshotSource(configuration.IsMonitoredKey)
                    .CaptureResolved(resolvedRoots), cancellationToken);
                _ = repository.ReconcileAndComplete(baseline, members);
                logger.LogInformation("Completed registry baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}",
                    baseline.Id, baseline.ItemCount, baseline.ScopeHash);
                repository.CompactAfterCompletion(baseline, retention.BaselineGenerations);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Registry baseline {BaselineId} failed", baseline.Id); return false; }
        }

        internal async Task<bool> RunFileSystemSnapshot(CancellationToken cancellationToken)
        {
            var configuration = settings.Capture();
            var baseline = repository.Begin(BaselineSource.FileSystem, configuration.ScopeHash,
                SourceIdentityProvider.FileSystem(configuration.MonitoredPaths));
            try
            {
                var source = new FileSystemSnapshotSource(configuration.HashLimitMB, configuration.IsMonitoredPath);
                var observation = await Task.Run(() => CursorlessSnapshotConvergence.Capture(
                    () => source.Capture(configuration.MonitoredPaths)), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                baseline.ConsistencyMethod = "CursorlessConsecutiveAgreement";
                baseline.ObservationPasses = observation.Passes;
                _ = repository.ReconcileAndCompleteAfterConvergence(baseline, observation.Members);
                logger.LogInformation("Completed filesystem baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}",
                    baseline.Id, baseline.ItemCount, baseline.ScopeHash);
                repository.CompactAfterCompletion(baseline, retention.BaselineGenerations);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Filesystem baseline {BaselineId} failed", baseline.Id); return false; }
        }

        private static Channel<SnapshotRequest> BoundedRequests() => Channel.CreateBounded<SnapshotRequest>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        private static async Task<SnapshotRequest?> WaitForRequestOrDelay(ChannelReader<SnapshotRequest> reader,
            TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5);
            var timer = Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken);
            var available = reader.WaitToReadAsync(cancellationToken).AsTask();
            if (await Task.WhenAny(timer, available) == timer) return null;
            return reader.TryRead(out var request) ? request : null;
        }

        private static void DrainRequests(ChannelReader<SnapshotRequest> reader)
        { while (reader.TryRead(out _)) { } }

        internal static TimeSpan RetryDelay(int failures) =>
            TimeSpan.FromSeconds(Math.Min(300, 1 << Math.Min(Math.Max(1, failures) - 1, 8)));

        private sealed record SnapshotRequest(string Reason, string? AffectedScope);
    }
}
