using System;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Health;
using WinFIMLog.Configuration;

namespace WinFIMLog
{
    public partial class JobOrchestrator : BackgroundService
    {
        private readonly FileSystemDiscoveryJob _fsDiscovery;

        private readonly FileSystemMonitorJob _fsMonitor;

        private readonly ILogger<JobOrchestrator> _logger;

        private readonly RegistryMonitorJob _regMonitor;

        private readonly Settings _settings;
        private readonly HealthMetrics _metrics;
        private readonly IHealthReporter _health;

        public JobOrchestrator(ILogger<JobOrchestrator> logger,
                      FileSystemCaptureQueue capture,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx,
                      Settings settings,
                      IHealthReporter health,
                      HealthMetrics metrics)
        {
            _logger = logger;
            _settings = settings;
            _metrics = metrics;
            _health = health;
            _regMonitor = new RegistryMonitorJob(_logger, regStore, settings);
            // Discovery writes enriched records directly; live notifications use the capture queue.
            _fsDiscovery = new FileSystemDiscoveryJob(_logger, fsStore, ctx, settings);
            _fsMonitor = new FileSystemMonitorJob(_logger, capture, health, settings, ReconcileScope);
        }

        private void ReconcileScope(string scope)
        {
            // The current discovery reader accepts the configured scope as a whole. The affected
            // root remains explicit in health evidence; Phase 4 supplies a truly scoped snapshot.
            _logger.LogWarning("Starting reconciliation after source loss for scope {Scope}", scope);
            _ = Task.Run(_fsDiscovery.Start);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => ExecutableTask(stoppingToken);

        private async Task CleanupAsync(Task? fileSystemDiscoveryTask, Task? registryMonitorTask)
        {
            _fsMonitor.Stop();

            if (_settings.EnableRegistryMonitoring)
            {
                _regMonitor.Stop();
            }

            try
            {
                if (fileSystemDiscoveryTask != null)
                {
                    await fileSystemDiscoveryTask;
                }

                if (registryMonitorTask != null)
                {
                    await registryMonitorTask;
                }
            }
            finally
            {
                _fsMonitor.Dispose();
                _regMonitor.Dispose();
            }
        }

        // Workaround for synchronous actions
        // Reference: https://blog.stephencleary.com/2020/05/backgroundservice-gotcha-startup.html
        private async Task ExecutableTask(CancellationToken stoppingToken)
        {
            Task? fileSystemDiscoveryTask = null;
            Task? registryMonitorTask = null;
            Task? scopeRefreshTask = null;

            try
            {
                // Recurring Tier 0 snapshots own completeness. The legacy discovery flag is
                // retained only for upgrade compatibility and is never an execution gate.
                _fsMonitor.Start();
                scopeRefreshTask = RefreshScopeAsync(stoppingToken);

                if (_settings.EnableRegistryMonitoring)
                {
                    // TraceEventSource.Process is synchronous and blocks until the ETW session is
                    // stopped. Run it independently so heartbeat and host cancellation can proceed.
                    registryMonitorTask = Task.Run(_regMonitor.Start, CancellationToken.None);
                }

                if (_settings.HeartbeatInterval <= 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation((int)HealthEventId.Heartbeat,
                        "HEARTBEAT Time={Time} ScopeHash={ScopeHash} QueueDepth={QueueDepth} OldestItemAgeMs={OldestItemAgeMs} Accepted={Accepted} Processed={Processed} Dropped={Dropped} EnrichmentFailures={EnrichmentFailures}",
                        DateTimeOffset.Now, _settings.ScopeHash, _metrics.QueueDepth, _metrics.OldestItemAge.TotalMilliseconds,
                        _metrics.Accepted, _metrics.Processed, _metrics.Dropped, _metrics.EnrichmentFailures);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.HeartbeatInterval), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            finally
            {
                await CleanupAsync(fileSystemDiscoveryTask, registryMonitorTask);
                if (scopeRefreshTask != null)
                {
                    try { await scopeRefreshTask; }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                }
            }
        }

        private async Task RefreshScopeAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.ScopeReresolutionInterval), stoppingToken);
                try
                {
                    var result = _settings.Reload();
                    if (!result.Changed) continue;
                    _fsMonitor.Reconfigure();
                    _health.ConfigurationChanged(result.PreviousHash, result.CurrentHash);
                }
                catch (ConfigurationValidationException exception)
                {
                    // Keep the last valid scope active. A policy error must be visible but must
                    // never silently replace or remove existing coverage.
                    _health.CoverageGap("Configuration", _settings.ScopeHash, $"Rejected:{exception.Message}", 0);
                }
            }
        }

        private async Task StartFilesystemDiscoveryAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation(
                    "File discovery not completed. Initiating file system discovery. It will take time.");
                await Task.Run(_fsDiscovery.Start, stoppingToken);
                _settings.IsFileDiscoveryCompleted = true;
                _logger.LogInformation("File system discovery completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file system discovery.");
            }
        }
    }
}
