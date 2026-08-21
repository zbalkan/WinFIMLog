using System;
using System.Collections.Generic;
using LiteDB;

namespace WinFIMLog.Events
{
    [BsonSourceGenerated]
    public sealed class EventOutboxRecord
    {
        // LiteDB materialises records through reflection. Keep this constructor explicit so
        // native-AOT publishing can preserve it for the mapper (see LiteDbContext).
        public EventOutboxRecord()
        { }

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public int DeliveryAttempts { get; set; }
        public bool Error { get; set; }
        public ushort EventId { get; set; }
        public Dictionary<string, object?> Fields { get; set; } = [];
        public string Id { get; set; } = string.Empty;
        public string? LastError { get; set; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string RecordType { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public string ScopeHash { get; set; } = string.Empty;

        public EventContract ToEventContract() => new(
            SchemaVersion, EventId, RecordType, OccurredAt, Id, ScopeHash, Fields);
    }
}
