using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using WinFIMLog.Health;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Jobs
{
    internal sealed class SnapshotHealthMonitor(
        SnapshotHealthState state,
        Settings settings,
        IHealthReporter health) : BackgroundService
    {
        private bool fileSystemOverdue;
        private bool registryOverdue;

        internal void Check(BaselineSource source)
        {
            var configuration = settings.Capture();
            var interval = TimeSpan.FromSeconds(source == BaselineSource.FileSystem
                ? configuration.FileSystemSnapshotInterval : configuration.RegistrySnapshotInterval);
            var lastSuccess = source == BaselineSource.FileSystem
                ? state.FileSystemLastSuccess : state.RegistryLastSuccess;
            var started = source == BaselineSource.FileSystem
                ? state.FileSystemStarted : state.RegistryStarted;
            var overdue = lastSuccess is not null
                ? DateTimeOffset.UtcNow - lastSuccess > interval + interval
                : started is not null && DateTimeOffset.UtcNow - started > interval;
            ref var reported = ref Overdue(source);
            if (overdue && !reported)
            {
                reported = true;
                health.CoverageGap($"{source}Snapshot", configuration.ScopeHash, "BaselineOverdue", 0);
            }
            else if (!overdue && reported)
            {
                reported = false;
                health.SourceRecovered($"{source}Snapshot", configuration.ScopeHash, "BaselineCurrent");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Check(BaselineSource.FileSystem);
                if (settings.EnableRegistryMonitoring) Check(BaselineSource.Registry);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private ref bool Overdue(BaselineSource source)
        { if (source == BaselineSource.FileSystem) return ref fileSystemOverdue; return ref registryOverdue; }
    }
}
