using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.IO;
using WinFIMLog.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

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

            CreateHostBuilder(args).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Information);

                    // Add Serilog for event logging
                    _ = logging.AddSerilog(new LoggerConfiguration()
                        .WriteTo.EventLog("WinFIMLog", "WinFIMLog", manageEventSource: true, eventIdProvider: new EventIdProvider())
                        .CreateLogger());
                })
                .ConfigureAppConfiguration(configuration =>
                {
                    _ = configuration.AddWindowsRegistry(Registry.RootName, Registry.Hive, false);
                })
                .ConfigureServices(services =>
                {
                    _ = services.AddSingleton<Settings>();
                    _ = services.AddOptions<LiteDbOptions>()
                        .Configure<Settings>((options, settings) => options.DatabasePath = settings.DatabasePath);
                    _ = services.AddSingleton<ILiteDbContext, LiteDbContext>();
                    _ = services.AddSingleton<BackgroundWorkerQueue>();
                    _ = services.AddSingleton<IBuffer<FileSystemChange>, FileSystemChangeBuffer>();
                    _ = services.AddSingleton<IBuffer<RegistryChange>, RegistryChangeBuffer>();
                    _ = services.AddHostedService<JobOrchestrator>();
                    _ = services.AddHostedService<BufferConsumer>();
                })
                .UseWindowsService();
    }
}
