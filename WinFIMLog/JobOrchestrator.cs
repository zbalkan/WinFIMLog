using System;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;
using WinFIMLog.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WinFIMLog
{
    public partial class JobOrchestrator : BackgroundService
    {
        private readonly FileSystemDiscoveryJob _fsDiscovery;

        private readonly FileSystemMonitorJob _fsMonitor;

        private readonly ILogger<JobOrchestrator> _logger;

        private readonly RegistryMonitorJob _regMonitor;

        private readonly Settings _settings;

        public JobOrchestrator(ILogger<JobOrchestrator> logger,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx,
                      Settings settings)
        {
            _logger = logger;
            _settings = settings;
            _fsMonitor = new FileSystemMonitorJob(_logger, fsStore, ctx, settings);
            _regMonitor = new RegistryMonitorJob(_logger, regStore, settings);
            _fsDiscovery = new FileSystemDiscoveryJob(_logger, fsStore, ctx, settings);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => ExecutableTask(stoppingToken);

        private void Cleanup()
        {
            // Cleanup members here
            _fsMonitor.Stop();
            _fsMonitor.Dispose();

            if (_settings.EnableRegistryMonitoring)
            {
                _regMonitor.Stop();
                _regMonitor.Dispose();
            }
        }

        // Workaround for synchronous actions
        // Reference: https://blog.stephencleary.com/2020/05/backgroundservice-gotcha-startup.html
        private async Task ExecutableTask(CancellationToken stoppingToken)
        {
            _ = NativeMethods.SetConsoleCtrlHandler(Handler, true);

            try
            {
                if (_settings.EnableLocalDatabase && !_settings.IsFileDiscoveryCompleted)
                {
                    _ = StartFilesystemDiscoveryAsync(stoppingToken);
                }
                _fsMonitor.Start();

                if (_settings.EnableRegistryMonitoring)
                {
                    _regMonitor.Start();
                }

                if (_settings.HeartbeatInterval <= 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("HEARTBEAT: Worker running at: {time}", DateTimeOffset.Now);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.HeartbeatInterval), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            finally
            {
                Cleanup();
            }
        }

        private bool Handler(CtrlType signal)
        {
            switch (signal)
            {
                case CtrlType.CtrlBreakEvent:
                case CtrlType.CtrlCEvent:
                case CtrlType.CtrlLogoffEvent:
                case CtrlType.CtrlShutdownEvent:
                case CtrlType.CtrlCloseEvent:
                    _logger.LogInformation("Worker stopped at: {time}", DateTimeOffset.Now);
                    Cleanup();
                    Environment.Exit(0);
                    return false;

                default:
                    return false;
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
