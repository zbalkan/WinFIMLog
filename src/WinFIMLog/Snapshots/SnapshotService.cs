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
using WinFIMLog.Integrity;

namespace WinFIMLog.Snapshots
{
    /// <summary>Coordinates independent, bounded Tier 0 source schedulers.</summary>
    public sealed class SnapshotService : BackgroundService, ISnapshotCoordinator
    {
        private readonly FileSystemBaselineAvailability fileSystemBaselineAvailability;
        private readonly Channel<SnapshotRequest> fileSystemRequests = BoundedRequests();
        private readonly IHealthReporter health;
        private readonly ILogger<SnapshotService> logger;
        private readonly Channel<SnapshotRequest> registryRequests = BoundedRequests();
        private readonly BaselineRepository repository;
        private readonly RetentionOptions retention;
        private readonly Settings settings;
        private readonly SnapshotHealthState state;
        private readonly ITpmBaselineIntegrity tpmIntegrity;
        private readonly IFileSystemSnapshotProvider vssSnapshots;
        private readonly IVssDriveInventory vssDriveInventory;

        public SnapshotService(BaselineRepository repository, Settings settings,
            ILogger<SnapshotService> logger, IHealthReporter health, SnapshotHealthState state,
            IOptions<RetentionOptions> retention, FileSystemBaselineAvailability fileSystemBaselineAvailability,
            ITpmBaselineIntegrity tpmIntegrity)
            : this(repository, settings, logger, health, state, retention, fileSystemBaselineAvailability,
                tpmIntegrity, new DisabledFileSystemSnapshotProvider(), new VssMftDriveInventory())
        { }

        public SnapshotService(BaselineRepository repository, Settings settings,
            ILogger<SnapshotService> logger, IHealthReporter health, SnapshotHealthState state,
            IOptions<RetentionOptions> retention, FileSystemBaselineAvailability fileSystemBaselineAvailability,
            ITpmBaselineIntegrity tpmIntegrity, IFileSystemSnapshotProvider vssSnapshots, IVssDriveInventory vssDriveInventory)
        {
            this.repository = repository;
            this.settings = settings;
            this.logger = logger;
            this.health = health;
            this.state = state;
            this.retention = retention.Value;
            this.fileSystemBaselineAvailability = fileSystemBaselineAvailability;
            this.tpmIntegrity = tpmIntegrity;
            this.vssSnapshots = vssSnapshots;
            this.vssDriveInventory = vssDriveInventory;
        }

        internal int PendingFileSystemRequests => fileSystemRequests.Reader.Count;
        internal int PendingRegistryRequests => registryRequests.Reader.Count;

        public void RequestFileSystemSnapshot(string reason, string? affectedScope = null) =>
            fileSystemRequests.Writer.TryWrite(new SnapshotRequest(reason, affectedScope));

        public void RequestRegistrySnapshot(string reason, string? affectedScope = null) =>
            registryRequests.Writer.TryWrite(new SnapshotRequest(reason, affectedScope));

        public void RequestScopeSnapshot(string reason)
        {
            RequestFileSystemSnapshot(reason);
            if (settings.EnableRegistryMonitoring)
            {
                RequestRegistrySnapshot(reason);
            }
        }

        internal static TimeSpan RetryDelay(int failures) =>
            TimeSpan.FromSeconds(Math.Min(300, 1 << Math.Min(Math.Max(1, failures) - 1, 8)));

        internal async Task<bool> RunFileSystemSnapshot(CancellationToken cancellationToken)
        {
            var configuration = settings.Capture();
            var fallbackAlgorithm = FileSystemBaselineAvailability.AlgorithmVersion(configuration);
            var tpmEnabled = IsTpmIntegrityAvailable(configuration, fallbackAlgorithm);
            var baseline = repository.Begin(BaselineSource.FileSystem, configuration.ScopeHash,
                SourceIdentityProvider.FileSystem(configuration.MonitoredPaths),
                algorithm: tpmEnabled ? BaselineAlgorithm.TpmRsaPssSha256 : fallbackAlgorithm);
            try
            {
                IReadOnlyList<BaselineMember> members;
                if (configuration.EnableVssFileSystemSnapshots)
                {
                    var groups = VssDriveGroup.GroupByDrive(configuration.MonitoredPaths);
                    using var slots = new SemaphoreSlim(Math.Min(configuration.DiscoveryConcurrency, groups.Count));
                    var jobs = new Task<IReadOnlyList<BaselineMember>>[groups.Count];
                    for (var i = 0; i < groups.Count; i++) jobs[i] = RunVssDriveJob(groups[i], configuration, slots, cancellationToken);
                    var results = await Task.WhenAll(jobs).ConfigureAwait(false);
                    var aggregate = new List<BaselineMember>(); foreach (var result in results) aggregate.AddRange(result);
                    members = aggregate; baseline.ConsistencyMethod = "VssBackupSnapshotPerDrive"; baseline.ObservationPasses = 1;
                }
                else
                {
                    var source = new FileSystemSnapshotSource(configuration.HashLimitMB, configuration.IsMonitoredPath);
                    var observation = await Task.Run(() => CursorlessSnapshotConvergence.Capture(() => source.Capture(configuration.MonitoredPaths)), cancellationToken);
                    members = observation.Members; baseline.ConsistencyMethod = "CursorlessConsecutiveAgreement"; baseline.ObservationPasses = observation.Passes;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (tpmEnabled && !tpmIntegrity.TrySeal(baseline, members, out var tpmReason)) ApplyTpmFallback(baseline, configuration.ScopeHash, tpmReason, fallbackAlgorithm);
                if (configuration.EnableVssFileSystemSnapshots) _ = repository.ReconcileAndComplete(baseline, members);
                else _ = repository.ReconcileAndCompleteAfterConvergence(baseline, members);
                fileSystemBaselineAvailability.Refresh(configuration);
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

        private async Task<IReadOnlyList<BaselineMember>> RunVssDriveJob(VssDriveGroup group, EffectiveSettings configuration, SemaphoreSlim slots, CancellationToken cancellationToken)
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { using var snapshot = await vssSnapshots.CreateAsync(group, cancellationToken).ConfigureAwait(false); var members = await Task.Run(() => vssDriveInventory.Capture(group, snapshot, configuration), cancellationToken).ConfigureAwait(false); await snapshot.CompleteAsync(cancellationToken).ConfigureAwait(false); return members; }
            finally { slots.Release(); }
        }

        internal async Task<bool> RunRegistrySnapshot(CancellationToken cancellationToken)
        {
            var configuration = settings.Capture();
            var tpmEnabled = IsTpmIntegrityAvailable(configuration, FallbackAlgorithmFor(BaselineSource.Registry));
            IReadOnlyList<string> resolvedRoots;
            try { resolvedRoots = RegistrySnapshotSource.ResolveRoots(configuration.MonitoredKeys); }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not resolve the active-user registry hive manifest");
                return false;
            }
            var baseline = repository.Begin(BaselineSource.Registry, configuration.ScopeHash,
                SourceIdentityProvider.RegistryResolved(resolvedRoots),
                algorithm: tpmEnabled ? BaselineAlgorithm.TpmRsaPssSha256 : FallbackAlgorithmFor(BaselineSource.Registry));
            baseline.ConsistencyMethod = "ResolvedLoadedHiveManifest";
            baseline.ObservationPasses = 1;
            try
            {
                var members = await Task.Run(() => new RegistrySnapshotSource(configuration.IsMonitoredKey)
                    .CaptureResolved(resolvedRoots), cancellationToken);
                if (tpmEnabled && !tpmIntegrity.TrySeal(baseline, members, out var tpmReason))
                {
                    ApplyTpmFallback(baseline, configuration.ScopeHash, tpmReason);
                }
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

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
                                    RunSourceLoop(BaselineSource.FileSystem, fileSystemRequests.Reader, stoppingToken),
            RunSourceLoop(BaselineSource.Registry, registryRequests.Reader, stoppingToken));

        internal static BaselineAlgorithm FallbackAlgorithmFor(BaselineSource source) => source switch
        {
            BaselineSource.FileSystem => BaselineAlgorithm.Sha256,
            BaselineSource.Registry => BaselineAlgorithm.RegistryV2,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported baseline source.")
        };

        private bool IsTpmIntegrityAvailable(EffectiveSettings configuration, BaselineAlgorithm fallbackAlgorithm)
        {
            if (configuration.TpmIntegrityMode != TpmIntegrityMode.PlatformRsaPssSha256)
            {
                return false;
            }

            if (tpmIntegrity.TryPrepare(out var reason))
            {
                return true;
            }

            health.TpmIntegrityUnavailable(configuration.ScopeHash, reason, fallbackAlgorithm);
            logger.LogError("TPM integrity policy is enabled but unavailable. Falling back to {FallbackAlgorithm}: {Reason}",
                fallbackAlgorithm, reason);
            return false;
        }

        private void ApplyTpmFallback(BaselineMetadata baseline, string scopeHash, string reason, BaselineAlgorithm? requestedFallback = null)
        {
            var fallbackAlgorithm = requestedFallback ?? FallbackAlgorithmFor(baseline.Source);
            baseline.Algorithm = fallbackAlgorithm;
            baseline.IntegrityAlgorithm = null;
            baseline.IntegrityManifestHash = null;
            baseline.IntegrityPublicKey = null;
            baseline.IntegritySignature = null;
            repository.RestoreFallbackApplicability(baseline);
            health.TpmIntegrityUnavailable(scopeHash, reason, fallbackAlgorithm);
            logger.LogError("TPM integrity signing failed. Baseline {BaselineId} will use {FallbackAlgorithm}: {Reason}",
                baseline.Id, fallbackAlgorithm, reason);
        }

        private static Channel<SnapshotRequest> BoundedRequests() => Channel.CreateBounded<SnapshotRequest>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        private static void DrainRequests(ChannelReader<SnapshotRequest> reader)
        { while (reader.TryRead(out _)) { } }

        private static async Task<SnapshotRequest?> WaitForRequestOrDelay(ChannelReader<SnapshotRequest> reader,
            TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.FromSeconds(5))
            {
                delay = TimeSpan.FromSeconds(5);
            }

            var timer = Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken);
            var available = reader.WaitToReadAsync(cancellationToken).AsTask();
            if (await Task.WhenAny(timer, available) == timer)
            {
                return null;
            }

            return reader.TryRead(out var request) ? request : null;
        }

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
                    if (request is null)
                    {
                        continue;
                    }

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
                    {
                        health.SourceRecovered($"{source}Snapshot", configuration.ScopeHash,
                            $"CompletedAfter{failures}Failures");
                    }

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

        private sealed record SnapshotRequest(string Reason, string? AffectedScope);
        private sealed class DisabledFileSystemSnapshotProvider : IFileSystemSnapshotProvider
        { public Task<IFileSystemSnapshot> CreateAsync(VssDriveGroup driveGroup, CancellationToken cancellationToken = default) => throw new InvalidOperationException("A VSS snapshot provider was not configured."); }
    }
}
