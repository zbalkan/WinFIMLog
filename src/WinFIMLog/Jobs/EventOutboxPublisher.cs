using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Events;
using WinFIMLog.IO;

namespace WinFIMLog.Jobs
{
    internal sealed class EventOutboxPublisher(
        EventOutboxRepository outbox,
        IEventRecordWriter writer,
        ILogger<EventOutboxPublisher> logger) : BackgroundService
    {
        internal bool PublishReady()
        {
            var worked = false;
            foreach (var item in outbox.Ready(DateTimeOffset.UtcNow))
            {
                worked = true;
                if (string.IsNullOrWhiteSpace(item.RecordType))
                {
                    outbox.DiscardInvalid(item, "EmptyRecordType");
                    logger.LogWarning(
                        "Event outbox record {RecordId} has an empty record type and was discarded",
                        item.Id);
                    continue;
                }
                try
                {
                    var record = item.ToEventContract();
                    writer.Write(record, item.Error);
                    outbox.Delivered(item);
                }
                catch (Exception exception)
                {
                    outbox.Failed(item, exception);
                    logger.LogError(exception,
                        "Event outbox record {RecordId} delivery attempt {Attempt} failed",
                        item.Id, item.DeliveryAttempts);
                }
            }
            return worked;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = PublishReady();
                if (!worked) await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
        }
    }
}
