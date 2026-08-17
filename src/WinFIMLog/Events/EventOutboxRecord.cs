using System;
using System.Collections.Generic;

namespace WinFIMLog.Events
{
    public sealed class EventOutboxRecord
    {
        // LiteDB materialises records through reflection. Keep this constructor explicit so
        // native-AOT publishing can preserve it for the mapper (see LiteDbContext).
        public EventOutboxRecord() { }

        public string Id { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public ushort EventId { get; set; }
        public string RecordType { get; set; } = string.Empty;
        public DateTimeOffset OccurredAt { get; set; }
        public string ScopeHash { get; set; } = string.Empty;
        public Dictionary<string, object?> Fields { get; set; } = [];
        public EventChannel Channel { get; set; }
        public bool Error { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public int DeliveryAttempts { get; set; }
        public string? LastError { get; set; }

        public EventContract ToEventContract() => new(
            SchemaVersion, EventId, RecordType, OccurredAt, Id, ScopeHash, Fields, Channel);
    }
}
