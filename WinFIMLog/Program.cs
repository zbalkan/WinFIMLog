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
                .ConfigureServices(services =>
                {
                    _ = services.Configure<LiteDbOptions>(options => options.DatabasePath = Settings.Instance.DatabasePath);
                    _ = services.AddSingleton<ILiteDbContext, LiteDbContext>();
                    _ = services.AddSingleton<BackgroundWorkerQueue>();
                    _ = services.AddSingleton<IBuffer<FileSystemChange>, FileSystemChangeBuffer>();
                    _ = services.AddSingleton<IBuffer<RegistryChange>, RegistryChangeBuffer>();
                    _ = services.AddHostedService<JobOrchestrator>();
                    _ = services.AddHostedService<BufferConsumer>();

                    IConfiguration configuration = new ConfigurationBuilder()
                    .AddWindowsRegistry(Registry.RootName, Registry.Hive, false)
                    .Build();
                })
                .UseWindowsService();
    }
}
