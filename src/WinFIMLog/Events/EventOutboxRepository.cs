using System;
using System.Collections.Generic;
using System.Linq;
using WinFIMLog.Data;

namespace WinFIMLog.Events
{
    public sealed class EventOutboxRepository
    {
        private readonly ILiteDbContext context;

        public EventOutboxRepository(ILiteDbContext context) => this.context = context;

        public void Enqueue(EventContract record, bool error = false) =>
            EnqueueBatch([(record, error)]);

        public void EnqueueBatch(IEnumerable<(EventContract Record, bool Error)> records, Action? projection = null)
        {
            var materialised = records.ToList();
            if (!context.ExecuteTransaction(() =>
            {
                projection?.Invoke();
                foreach (var item in materialised)
                {
                    if (context.EventOutbox.Exists(x => x.Id == item.Record.RecordId)) continue;
                    context.EventOutbox.Insert(new EventOutboxRecord
                    {
                        Id = item.Record.RecordId,
                        Payload = item.Record.ToJson(),
                        Error = item.Error,
                        CreatedAt = DateTimeOffset.UtcNow,
                        NextAttemptAt = DateTimeOffset.MinValue
                    });
                }
            })) throw new InvalidOperationException("The evidence/outbox transaction did not commit.");
        }

        public IReadOnlyList<EventOutboxRecord> Ready(DateTimeOffset now, int limit = 200) =>
            context.EventOutbox.Query()
                .Where(x => x.DeliveredAt == null && x.NextAttemptAt <= now)
                .OrderBy(x => x.CreatedAt).Limit(limit).ToList();

        public long PendingCount => context.EventOutbox.Count(x => x.DeliveredAt == null);
        public DateTimeOffset? OldestPending => context.EventOutbox.Query()
            .Where(x => x.DeliveredAt == null).OrderBy(x => x.CreatedAt).FirstOrDefault()?.CreatedAt;

        public void Delivered(EventOutboxRecord item)
        {
            item.DeliveryAttempts++;
            item.DeliveredAt = DateTimeOffset.UtcNow;
            item.LastError = null;
            if (!context.ExecuteTransaction(() => context.EventOutbox.Update(item)))
                throw new InvalidOperationException("Could not commit Event Log delivery acknowledgement.");
        }

        public void Failed(EventOutboxRecord item, Exception exception)
        {
            item.DeliveryAttempts++;
            item.LastError = exception.GetType().Name;
            var delaySeconds = Math.Min(300, 1 << Math.Min(item.DeliveryAttempts - 1, 8));
            item.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            if (!context.ExecuteTransaction(() => context.EventOutbox.Update(item)))
                throw new InvalidOperationException("Could not commit Event Log delivery failure state.");
        }

        public int DeleteDeliveredBefore(DateTimeOffset cutoff)
        {
            var deleted = 0;
            if (!context.ExecuteTransaction(() => deleted =
                context.EventOutbox.DeleteMany(x => x.DeliveredAt != null && x.DeliveredAt < cutoff)))
                throw new InvalidOperationException("Could not commit Event Log outbox retention.");
            return deleted;
        }
    }
}
