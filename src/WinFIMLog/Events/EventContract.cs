using System;
using System.Collections.Generic;
using WinFIMLog.IO;

namespace WinFIMLog.Events
{
    /// <summary>The stable event envelope used for durable delivery and Windows Event Log rendering.</summary>
    public sealed record EventContract(
        int SchemaVersion,
        ushort EventId,
        string RecordType,
        DateTimeOffset OccurredAt,
        string RecordId,
        string ScopeHash,
        IReadOnlyDictionary<string, object?> Fields)
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>Renders the event as a human-readable key-value record without JSON serialization.</summary>
        internal string FormatEventLogMessage() => EventLogMessageFormatter.Format(this);

        public static EventContract Create(ushort eventId, string recordType, string recordId,
            string scopeHash, IReadOnlyDictionary<string, object?> fields)
        {
            EventIdCatalog.Validate(eventId, recordType);
            return new(CurrentSchemaVersion, eventId, recordType, DateTimeOffset.UtcNow, recordId,
                scopeHash, fields);
        }

        public static bool IsSupported(int schemaVersion) => schemaVersion == CurrentSchemaVersion;
    }

    /// <summary>Formats event records directly into one final Windows Event Log message string.</summary>
    internal static class EventLogMessageFormatter
    {
        public static string Format(EventContract record)
        {
            ArgumentNullException.ThrowIfNull(record);

            Span<char> initialBuffer = stackalloc char[512];
            var buffer = new PooledCharBuffer(initialBuffer);
            try
            {
                AppendField(ref buffer, "SchemaVersion", record.SchemaVersion, hasPrevious: false);
                AppendField(ref buffer, "EventId", record.EventId, hasPrevious: true);
                AppendField(ref buffer, "RecordType", record.RecordType, hasPrevious: true);
                AppendField(ref buffer, "OccurredAt", record.OccurredAt, hasPrevious: true);
                AppendField(ref buffer, "RecordId", record.RecordId, hasPrevious: true);
                AppendField(ref buffer, "ScopeHash", record.ScopeHash, hasPrevious: true);

                foreach (var field in record.Fields)
                {
                    AppendField(ref buffer, field.Key, field.Value, hasPrevious: true);
                }

                return buffer.ToString();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private static void AppendField(ref PooledCharBuffer buffer, ReadOnlySpan<char> name, object? value, bool hasPrevious)
        {
            if (hasPrevious)
            {
                buffer.Append('\n');
            }

            AppendDisplayName(ref buffer, name);
            buffer.Append(": ");
            AppendValue(ref buffer, value);
        }

        private static void AppendDisplayName(ref PooledCharBuffer buffer, ReadOnlySpan<char> name)
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

        private static void AppendValue(ref PooledCharBuffer buffer, object? value)
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
                default:
                    buffer.Append(value.ToString() ?? "None");
                    break;
            }
        }
    }
}
