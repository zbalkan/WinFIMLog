using System;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Health;
using WinFIMLog.Configuration;
using WinFIMLog.Snapshots;

namespace WinFIMLog
{
    public partial class JobOrchestrator : BackgroundService
    {
        private readonly FileSystemMonitorJob _fsMonitor;

        private readonly ILogger<JobOrchestrator> _logger;

        private readonly RegistryMonitorJob _regMonitor;

        private readonly Settings _settings;
        private readonly HealthMetrics _metrics;
        private readonly IHealthReporter _health;
        private readonly ISnapshotCoordinator _snapshots;

        public JobOrchestrator(ILogger<JobOrchestrator> logger,
                      FileSystemCaptureQueue capture,
                      IBuffer<RegistryChange> regStore,
                      Settings settings,
                      IHealthReporter health,
                      HealthMetrics metrics,
                      ISnapshotCoordinator snapshots)
        {
            _logger = logger;
            _settings = settings;
            _metrics = metrics;
            _health = health;
            _snapshots = snapshots;
            _regMonitor = new RegistryMonitorJob(_logger, regStore, settings, snapshots);
            _fsMonitor = new FileSystemMonitorJob(_logger, capture, health, settings, snapshots);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => ExecutableTask(stoppingToken);

        private async Task CleanupAsync(Task? fileSystemMonitorTask, Task? registryMonitorTask)
        {
            try
            {
                if (fileSystemMonitorTask != null)
                {
                    await fileSystemMonitorTask;
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
            Task? fileSystemMonitorTask = null;
            Task? registryMonitorTask = null;
            Task? scopeRefreshTask = null;

            try
            {
                // Recurring Tier 0 snapshots own completeness. The legacy discovery flag is
                // retained only for upgrade compatibility and is never an execution gate.
                fileSystemMonitorTask = _fsMonitor.RunAsync(stoppingToken);
                scopeRefreshTask = RefreshScopeAsync(stoppingToken);

                if (_settings.EnableRegistryMonitoring)
                {
                    // TraceEventSource.Process is synchronous and blocks until the ETW session is
                    // stopped. Run it independently so heartbeat and host cancellation can proceed.
                    registryMonitorTask = RunRegistryMonitorWithRecoveryAsync(stoppingToken);
                }

                if (_settings.HeartbeatInterval <= 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    _health.Heartbeat(_metrics);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.HeartbeatInterval), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            finally
            {
                await CleanupAsync(fileSystemMonitorTask, registryMonitorTask);
                if (scopeRefreshTask != null)
                {
                    try { await scopeRefreshTask; }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                }
            }
        }

        private async Task RunRegistryMonitorWithRecoveryAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _regMonitor.RunAsync(stoppingToken);
                    if (stoppingToken.IsCancellationRequested) return;
                    throw new InvalidOperationException("Registry ETW source stopped unexpectedly.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    _health.CoverageGap("RegistryETW", "ConfiguredRegistryKeys",
                        $"SourceFailed:{exception.GetType().Name}");
                    _snapshots.RequestRegistrySnapshot("Registry ETW source failure", "ConfiguredRegistryKeys");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    _health.SourceRecovered("RegistryETW", "ConfiguredRegistryKeys", "SourceRestarted;ReconciliationRequested");
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
                    _snapshots.RequestScopeSnapshot("Effective configuration changed");
                }
                catch (ConfigurationValidationException exception)
                {
                    // Keep the last valid scope active. A policy error must be visible but must
                    // never silently replace or remove existing coverage.
                    _health.CoverageGap("Configuration", _settings.ScopeHash, $"Rejected:{exception.Message}", 0);
                }
            }
        }

    }
}
