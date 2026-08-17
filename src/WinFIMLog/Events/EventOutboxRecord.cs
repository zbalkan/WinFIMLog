using System;

namespace WinFIMLog.Events
{
    public sealed class EventOutboxRecord
    {
        // LiteDB materialises records through reflection. Keep this constructor explicit so
        // native-AOT publishing can preserve it for the mapper (see LiteDbContext).
        public EventOutboxRecord() { }

        public string Id { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public bool Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public int DeliveryAttempts { get; set; }
        public string? LastError { get; set; }
    }
}
