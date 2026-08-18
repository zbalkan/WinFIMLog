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
            JsonSerializer.Serialize(
                this with { Fields = FieldsForEventLog(Fields) },
                EventJsonContext.Default.EventContract);

        // ACL evidence stays as text while it is persisted in LiteDB. Convert valid
        // object/array payloads only in the short-lived event-log projection; storing a
        // JsonElement in the outbox would be reconstructed by LiteDB as an invalid default value.
        private static IReadOnlyDictionary<string, object?> FieldsForEventLog(
            IReadOnlyDictionary<string, object?> fields)
        {
            var eventLogFields = new Dictionary<string, object?>(fields.Count);
            foreach (var field in fields)
            {
                eventLogFields[field.Key] = field.Key is "currentAcl" or "previousAcl"
                    ? StructuredJsonOrOriginal(field.Value)
                    : field.Value;
            }
            return eventLogFields;
        }

        private static object? StructuredJsonOrOriginal(object? value)
        {
            // Records written by the earlier implementation can contain LiteDB-rehydrated,
            // undefined JsonElement values. Emit null for that unrecoverable evidence rather
            // than retrying the same failed event indefinitely.
            if (value is JsonElement { ValueKind: JsonValueKind.Undefined })
            {
                return null;
            }

            if (value is not string text)
            {
                return value;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? document.RootElement.Clone()
                    : value;
            }
            catch (JsonException)
            {
                return value;
            }
        }

        public static EventContract Create(ushort eventId, string recordType, string recordId,
            string scopeHash, IReadOnlyDictionary<string, object?> fields,
            EventChannel channel = EventChannel.Operational)
        {
            EventIdCatalog.Validate(eventId, recordType, channel);
            return new(CurrentSchemaVersion, eventId, recordType, DateTimeOffset.UtcNow, recordId,
                scopeHash, fields, channel);
        }

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
