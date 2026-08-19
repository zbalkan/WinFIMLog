using System;
using System.Collections.Generic;
using WinFIMLog.IO;

namespace WinFIMLog.Events
{
    public enum EventChannel
    {
        Operational,
        Baseline,
        Diagnostic
    }

    /// <summary>The stable event envelope used for durable delivery and Windows Event Log rendering.</summary>
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

        /// <summary>Renders the event as a human-readable key-value record without JSON serialization.</summary>
        internal string FormatEventLogMessage() => EventLogMessageFormatter.Format(this);

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

    /// <summary>Formats event records directly into one final Windows Event Log message string.</summary>
    internal static class EventLogMessageFormatter
    {
        public static string Format(EventContract record)
        {
            ArgumentNullException.ThrowIfNull(record);

            using var buffer = new PooledCharBuffer(512);
            AppendField(buffer, "SchemaVersion", record.SchemaVersion, hasPrevious: false);
            AppendField(buffer, "EventId", record.EventId, hasPrevious: true);
            AppendField(buffer, "RecordType", record.RecordType, hasPrevious: true);
            AppendField(buffer, "OccurredAt", record.OccurredAt, hasPrevious: true);
            AppendField(buffer, "RecordId", record.RecordId, hasPrevious: true);
            AppendField(buffer, "ScopeHash", record.ScopeHash, hasPrevious: true);
            AppendField(buffer, "Channel", record.Channel, hasPrevious: true);

            foreach (var field in record.Fields)
            {
                AppendField(buffer, field.Key, field.Value, hasPrevious: true);
            }

            return buffer.ToString();
        }

        private static void AppendField(PooledCharBuffer buffer, ReadOnlySpan<char> name, object? value, bool hasPrevious)
        {
            if (hasPrevious)
            {
                buffer.Append('\n');
            }

            AppendDisplayName(buffer, name);
            buffer.Append(": ");
            AppendValue(buffer, value);
        }

        private static void AppendDisplayName(PooledCharBuffer buffer, ReadOnlySpan<char> name)
        {
            var wasLowerCase = false;
            for (var index = 0; index < name.Length; index++)
            {
                var character = name[index];
                if (index != 0 && char.IsUpper(character) && wasLowerCase)
                {
                    buffer.Append(' ');
                }

                buffer.Append(index == 0 ? char.ToUpperInvariant(character) : character);
                wasLowerCase = char.IsLower(character);
            }
        }

        private static void AppendValue(PooledCharBuffer buffer, object? value)
        {
            switch (value)
            {
                case null:
                    buffer.Append("None");
                    break;
                case string text:
                    buffer.Append(text);
                    break;
                case bool boolean:
                    buffer.Append(boolean);
                    break;
                case int integer:
                    buffer.Append(integer);
                    break;
                case long integer:
                    buffer.Append(integer);
                    break;
                case ushort integer:
                    buffer.Append((int)integer);
                    break;
                case ulong integer:
                    buffer.Append(integer);
                    break;
                case double number:
                    buffer.Append(number);
                    break;
                case DateTime timestamp:
                    buffer.Append(timestamp);
                    break;
                case DateTimeOffset timestamp:
                    buffer.Append(timestamp);
                    break;
                case EventChannel channel:
                    buffer.Append(channel switch
                    {
                        EventChannel.Operational => "Operational",
                        EventChannel.Baseline => "Baseline",
                        EventChannel.Diagnostic => "Diagnostic",
                        _ => "Unknown"
                    });
                    break;
                default:
                    buffer.Append(value.ToString() ?? "None");
                    break;
            }
        }
    }
}
