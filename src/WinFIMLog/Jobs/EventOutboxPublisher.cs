using System;
using System.Text.Json;
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
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = PublishReady();
                if (!worked) await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
        }

        internal bool PublishReady()
        {
            var worked = false;
            foreach (var item in outbox.Ready(DateTimeOffset.UtcNow))
            {
                worked = true;
                if (string.IsNullOrWhiteSpace(item.Payload))
                {
                    outbox.DiscardInvalid(item, "EmptyPayload");
                    logger.LogError(
                        "Event outbox record {RecordId} has an empty payload and was discarded",
                        item.Id);
                    continue;
                }
                try
                {
                    var record = JsonSerializer.Deserialize(item.Payload, EventJsonContext.Default.EventContract)
                        ?? throw new InvalidOperationException("Outbox payload did not contain an event contract.");
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
    }
}
