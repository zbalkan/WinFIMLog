using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinFIMLog.Data;
using WinFIMLog.Events;

namespace WinFIMLog.Jobs
{
    internal sealed class StorageMaintenanceService(
        EventOutboxRepository outbox,
        IOptions<RetentionOptions> options,
        ILogger<StorageMaintenanceService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var days = Math.Max(1, options.Value.DeliveredOutboxDays);
                var deleted = outbox.DeleteDeliveredBefore(DateTimeOffset.UtcNow.AddDays(-days));
                if (deleted > 0)
                {
                    logger.LogInformation("Removed {Count} delivered Event Log outbox records", deleted);
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
