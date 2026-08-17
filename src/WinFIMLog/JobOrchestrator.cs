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
        private readonly FileSystemCaptureQueue _capture;
        private readonly object _registryLifecycleLock = new();
        private CancellationTokenSource? _registryMonitorCancellation;
        private Task? _registryMonitorTask;

        public JobOrchestrator(ILogger<JobOrchestrator> logger,
                      FileSystemCaptureQueue capture,
                      IBuffer<RegistryChange> regStore,
                      Settings settings,
                      IHealthReporter health,
                      HealthMetrics metrics,
                      ISnapshotCoordinator snapshots,
                      FileSystemBaselineAvailability fileSystemBaselineAvailability)
        {
            _logger = logger;
            _settings = settings;
            _metrics = metrics;
            _health = health;
            _snapshots = snapshots;
            _capture = capture;
            _regMonitor = new RegistryMonitorJob(_logger, regStore, settings, snapshots);
            _fsMonitor = new FileSystemMonitorJob(_logger, capture, health, settings, snapshots,
                fileSystemBaselineAvailability);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => ExecutableTask(stoppingToken);

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Wait for both monitor sources to stop before closing admission. The enrichment
            // worker then drains the completed raw channel before persistence is stopped.
            try { await base.StopAsync(cancellationToken); }
            finally
            {
                _capture.CompleteWriter();
                if (cancellationToken.IsCancellationRequested && _metrics.QueueDepth > 0)
                    _health.CoverageGap("ShutdownPipeline", _settings.ScopeHash,
                        "HostShutdownTimeout", _metrics.QueueDepth);
            }
        }

        private async Task CleanupAsync(Task? fileSystemMonitorTask)
        {
            try
            {
                if (fileSystemMonitorTask != null)
                {
                    await fileSystemMonitorTask;
                }

                await StopRegistryMonitorAsync();
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
                    StartRegistryMonitor(stoppingToken);
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
                if (scopeRefreshTask != null)
                {
                    try { await scopeRefreshTask; }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                }
                await CleanupAsync(fileSystemMonitorTask);
            }
        }

        private async Task RunRegistryMonitorWithRecoveryAsync(CancellationToken stoppingToken)
        {
            var recovering = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _regMonitor.RunAsync(stoppingToken, () =>
                    {
                        if (!recovering) return;
                        recovering = false;
                        _health.SourceRecovered("RegistryETW", "ConfiguredRegistryKeys",
                            "SourceRestarted;ReconciliationRequested");
                    });
                    if (stoppingToken.IsCancellationRequested) return;
                    throw new InvalidOperationException("Registry ETW source stopped unexpectedly.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    recovering = true;
                    _health.CoverageGap("RegistryETW", "ConfiguredRegistryKeys",
                        $"SourceFailed:{exception.GetType().Name}");
                    _snapshots.RequestRegistrySnapshot("Registry ETW source failure", "ConfiguredRegistryKeys");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private void StartRegistryMonitor(CancellationToken serviceToken)
        {
            lock (_registryLifecycleLock)
            {
                if (_registryMonitorTask is { IsCompleted: false }) return;
                _registryMonitorCancellation?.Dispose();
                _registryMonitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
                _registryMonitorTask = RunRegistryMonitorWithRecoveryAsync(_registryMonitorCancellation.Token);
            }
        }

        private async Task StopRegistryMonitorAsync()
        {
            Task? task;
            CancellationTokenSource? cancellation;
            lock (_registryLifecycleLock)
            {
                task = _registryMonitorTask;
                cancellation = _registryMonitorCancellation;
                _registryMonitorTask = null;
                _registryMonitorCancellation = null;
            }
            if (task is null) return;
            cancellation!.Cancel();
            try { await task; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { cancellation.Dispose(); }
        }

        private async Task ReconfigureRegistryMonitorAsync(CancellationToken serviceToken)
        {
            if (_settings.EnableRegistryMonitoring) StartRegistryMonitor(serviceToken);
            else await StopRegistryMonitorAsync();
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
                    await ReconfigureRegistryMonitorAsync(stoppingToken);
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
