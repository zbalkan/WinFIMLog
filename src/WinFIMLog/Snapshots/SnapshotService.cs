using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Snapshots
{
    /// <summary>Schedules Tier 0 scans independently of the legacy discovery flag.</summary>
    public sealed class SnapshotService : BackgroundService
    {
        private readonly BaselineRepository repository;
        private readonly Settings settings;
        private readonly ILogger<SnapshotService> logger;

        public SnapshotService(BaselineRepository repository, Settings settings, ILogger<SnapshotService> logger)
        { this.repository = repository; this.settings = settings; this.logger = logger; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Database deletion always leaves no complete baseline, so startup scans immediately;
            // FileDiscoveryCompleted is intentionally not consulted.
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunFileSystemSnapshot(stoppingToken);
                if (settings.EnableRegistryMonitoring) await RunRegistrySnapshot(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(settings.FileSystemSnapshotInterval), stoppingToken);
            }
        }

        internal async Task RunRegistrySnapshot(CancellationToken cancellationToken)
        {
            var identity = string.Join(";", settings.MonitoredKeys.Order(StringComparer.OrdinalIgnoreCase));
            var baseline = repository.Begin(BaselineSource.Registry, settings.ScopeHash, identity, algorithmVersion: "registry-v1");
            try
            {
                var members = await Task.Run(() => new RegistrySnapshotSource().Capture(settings.MonitoredKeys), cancellationToken);
                repository.ReconcileAndComplete(baseline, members);
                logger.LogInformation("Completed registry baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}", baseline.Id, baseline.ItemCount, baseline.ScopeHash);
            }
            catch (OperationCanceledException) { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Registry baseline {BaselineId} failed", baseline.Id); }
        }

        internal async Task RunFileSystemSnapshot(CancellationToken cancellationToken)
        {
            var identity = string.Join(";", settings.MonitoredPaths.Select(SourceIdentity));
            var baseline = repository.Begin(BaselineSource.FileSystem, settings.ScopeHash, identity);
            try
            {
                var source = new FileSystemSnapshotSource(settings.HashLimitMB);
                var first = await Task.Run(() => source.Capture(settings.MonitoredPaths), cancellationToken);
                baseline.Status = BaselineStatus.Reconciling;
                // No filesystem cursor exists. A second pass bounds changes overlapping enumeration.
                var second = await Task.Run(() => source.Capture(settings.MonitoredPaths), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                repository.ReconcileAndCompleteAfterSecondPass(baseline, first, second);
                logger.LogInformation("Completed filesystem baseline {BaselineId} with {ItemCount} members for ScopeHash {ScopeHash}",
                    baseline.Id, baseline.ItemCount, baseline.ScopeHash);
            }
            catch (OperationCanceledException) { repository.MarkInvalid(baseline, "Service stopped during scan"); throw; }
            catch (Exception exception)
            { repository.MarkInvalid(baseline, exception.GetType().Name); logger.LogError(exception, "Filesystem baseline {BaselineId} failed", baseline.Id); }
        }

        private static string SourceIdentity(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            if (!OperatingSystem.IsWindows()) return root;
            try { return $"{root}:{new DriveInfo(root).DriveFormat}:{new DriveInfo(root).TotalSize}"; }
            catch { return root; }
        }
    }
}
