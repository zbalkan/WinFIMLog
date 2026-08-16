using System;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Health;

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

            try
            {
                if (_settings.EnableLocalDatabase && !_settings.IsFileDiscoveryCompleted)
                {
                    fileSystemDiscoveryTask = StartFilesystemDiscoveryAsync(stoppingToken);
                }
                _fsMonitor.Start();

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
                        "HEARTBEAT Time={Time} QueueDepth={QueueDepth} OldestItemAgeMs={OldestItemAgeMs} Accepted={Accepted} Processed={Processed} Dropped={Dropped} EnrichmentFailures={EnrichmentFailures}",
                        DateTimeOffset.Now, _metrics.QueueDepth, _metrics.OldestItemAge.TotalMilliseconds,
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
