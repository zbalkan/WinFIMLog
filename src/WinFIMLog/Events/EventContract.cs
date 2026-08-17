using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinFIMLog.Events
{
    [JsonConverter(typeof(JsonStringEnumConverter<EventChannel>))]
    public enum EventChannel
    { Operational, Baseline, Diagnostic }

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

        internal string FormatEventLogMessage() =>
            JsonSerializer.Serialize(this, EventJsonContext.Default.EventContract);

        public static EventContract Create(ushort eventId, string recordType, string recordId,
            string scopeHash, IReadOnlyDictionary<string, object?> fields,
            EventChannel channel = EventChannel.Operational) =>
            new(CurrentSchemaVersion, eventId, recordType, DateTimeOffset.UtcNow, recordId,
                scopeHash, fields, channel);

        public static bool IsSupported(int schemaVersion) => schemaVersion == CurrentSchemaVersion;
    }

    // Fields is intentionally open-ended, so source generation cannot discover the
    // boxed scalar types that are assigned to it at runtime. Keep every scalar used
    // by event producers in the context; otherwise serialization fails when the
    // object converter encounters (for example) a boxed Int64 health metric.
    [JsonSerializable(typeof(EventContract))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class EventJsonContext : JsonSerializerContext;
}
