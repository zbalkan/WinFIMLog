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

        public JobOrchestrator(ILogger<JobOrchestrator> logger,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx)
        {
            _logger = logger;
            _fsMonitor = new FileSystemMonitorJob(_logger, fsStore, ctx);
            _regMonitor = new RegistryMonitorJob(_logger, regStore);
            _fsDiscovery = new FileSystemDiscoveryJob(_logger, fsStore, ctx);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => ExecutableTask(stoppingToken);

        private void Cleanup()
        {
            // Cleanup members here
            _fsMonitor.Stop();
            _fsMonitor.Dispose();

            if (Settings.Instance.EnableRegistryMonitoring)
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
                if (Settings.Instance.EnableLocalDatabase && !Settings.Instance.IsFileDiscoveryCompleted)
                {
                    _ = StartFilesystemDiscoveryAsync(stoppingToken);
                }
                _fsMonitor.Start();

                if (Settings.Instance.EnableRegistryMonitoring)
                {
                    _regMonitor.Start();
                }

                if (Settings.Instance.HeartbeatInterval <= 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("HEARTBEAT: Worker running at: {time}", DateTimeOffset.Now);
                    await Task.Delay(TimeSpan.FromSeconds(Settings.Instance.HeartbeatInterval), stoppingToken);
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
                Settings.Instance.IsFileDiscoveryCompleted = true;
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
