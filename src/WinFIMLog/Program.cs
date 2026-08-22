using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Attribution;
using WinFIMLog.Configuration;
using WinFIMLog.Data;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using WinFIMLog.IO;
using WinFIMLog.Jobs;
using WinFIMLog.Snapshots;
using WinFIMLog.Utils;

namespace WinFIMLog
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (ServiceInstaller.TryHandleCommand(args))
            {
                return;
            }

            using var host = CreateHostBuilder(args).Build();
            try
            {
                host.Run();
            }
            catch (OperationCanceledException exception)
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(Program).FullName!)
                    .LogInformation(exception, "Host lifetime cancelled; shutdown completed normally.");
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(static logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddProvider(new EventIdEventLogLoggerProvider("WinFIMLog", "WinFIMLog"));
                })
                .ConfigureAppConfiguration(static configuration => _ = configuration.AddWindowsRegistry(Registry.RootName, Registry.Hive, optional: false))
                .ConfigureServices(static services =>
                {
                    // Suppress console messages like "Application started. Press Ctrl+C to shut down.", "Hosting environment: Development", etc.
                    _ = services.Configure<ConsoleLifetimeOptions>(static options => options.SuppressStatusMessages = true);
                    _ = services.AddOptions<SaclAttributionOptions>()
                        .BindConfiguration("Attribution:Sacl");
                    _ = services.AddOptions<RetentionOptions>()
                        .BindConfiguration("Retention");
                    _ = services.AddSingleton<IAuditPolicyConformance, WindowsAuditPolicyConformance>();
                    _ = services.AddSingleton<Settings>();
                    _ = services.AddOptions<LiteDbOptions>()
                        .Configure<Settings>(static (options, settings) => options.DatabasePath = settings.DatabasePath);
                    _ = services.AddSingleton<ILiteDbContext, LiteDbContext>();
                    _ = services.AddSingleton<BackgroundWorkerQueue>();
                    _ = services.AddSingleton<IBuffer<FileSystemChange>, FileSystemChangeBuffer>();
                    _ = services.AddSingleton<IBuffer<RegistryChange>, RegistryChangeBuffer>();
                    _ = services.AddSingleton<HealthMetrics>();
                    _ = services.AddSingleton<SnapshotHealthState>();
                    _ = services.AddSingleton<IHealthReporter, HealthReporter>();
                    _ = services.AddSingleton<WindowsEventLogSink>();
                    _ = services.AddSingleton<IEventRecordWriter>(static provider => provider.GetRequiredService<WindowsEventLogSink>());
                    _ = services.AddSingleton<EventOutboxRepository>();
                    _ = services.AddSingleton<DurableEventOutboxSink>();
                    _ = services.AddSingleton<ILocalEventSink>(static provider => provider.GetRequiredService<DurableEventOutboxSink>());
                    // Registered first so the durable publisher stops after every producer.
                    _ = services.AddHostedService<EventOutboxPublisher>();
                    _ = services.AddHostedService<StorageMaintenanceService>();
                    _ = services.AddSingleton<FileSystemCaptureQueue>();
                    _ = services.AddSingleton<BaselineRepository>();
                    _ = services.AddSingleton<FileSystemBaselineAvailability>();
                    // Reject invalid settings before any source or snapshot hosted service starts.
                    _ = services.AddHostedService<SettingsStartupValidator>();
                    // Optional and deliberately independent of snapshot/completeness services.
                    _ = services.AddHostedService<SecurityAuditAttributionService>();
                    _ = services.AddSingleton<SnapshotService>();
                    _ = services.AddSingleton<ISnapshotCoordinator>(static provider => provider.GetRequiredService<SnapshotService>());
                    _ = services.AddHostedService(static provider => provider.GetRequiredService<SnapshotService>());
                    _ = services.AddHostedService<BaselineFindingPublisher>();
                    _ = services.AddHostedService<SnapshotHealthMonitor>();
                    // Hosted services are stopped in reverse registration order. Start the
                    // consumer first so monitors are stopped before the consumer drains buffers.
                    _ = services.AddHostedService<BufferConsumer>();
                    _ = services.AddHostedService<FileSystemEnrichmentWorker>();
                    _ = services.AddHostedService<JobOrchestrator>();
                })
                .UseWindowsService();
    }
}
