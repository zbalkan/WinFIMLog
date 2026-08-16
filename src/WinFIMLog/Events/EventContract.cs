using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFIMLog.Events
{
    [JsonConverter(typeof(JsonStringEnumConverter<EventChannel>))]
    public enum EventChannel { Operational, Baseline, Diagnostic }

    /// <summary>The stable, machine-readable Phase 5 transport envelope.</summary>
    public sealed record EventContract(
        int SchemaVersion,
        ushort EventId,
        string RecordType,
        DateTimeOffset OccurredAt,
        string RecordId,
        string ScopeHash,
        IReadOnlyDictionary<string, object?> Fields,
        EventChannel Channel = EventChannel.Operational)
    {
        public const int CurrentSchemaVersion = 1;

        public string ToJson() => JsonSerializer.Serialize(this, EventJsonContext.Default.EventContract);

        public static EventContract Create(ushort eventId, string recordType, string recordId,
            string scopeHash, IReadOnlyDictionary<string, object?> fields,
            EventChannel channel = EventChannel.Operational) =>
            new(CurrentSchemaVersion, eventId, recordType, DateTimeOffset.UtcNow, recordId,
                scopeHash, fields, channel);

        public static bool IsSupported(int schemaVersion) => schemaVersion == CurrentSchemaVersion;
    }

    [JsonSerializable(typeof(EventContract))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class EventJsonContext : JsonSerializerContext { }
}
