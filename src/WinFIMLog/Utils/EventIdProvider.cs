using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Utils
{
    internal sealed class EventIdProvider
    {
        public ushort ComputeEventId<TState>(LogLevel level, EventId eventId, TState state)
        {
            if (eventId.Id is > 0 and <= ushort.MaxValue) return (ushort)eventId.Id;

            if (level == LogLevel.Error || level == LogLevel.Critical) return 7770;

            if (level == LogLevel.Information &&
                state is IEnumerable<KeyValuePair<string, object?>> properties &&
                TryGetValue(properties, "changeType", out var changeType) &&
                TryGetValue(properties, "category", out var category))
            {
                return (changeType.ToUpperInvariant(), category.ToUpperInvariant()) switch
                {
                    ("FILESYSTEM", "CREATED") => 7776,
                    ("FILESYSTEM", "CHANGED") => 7777,
                    ("FILESYSTEM", "DELETED") => 7778,
                    ("REGISTRY", "CREATED") => 7786,
                    ("REGISTRY", "CHANGED") => 7787,
                    ("REGISTRY", "DELETED") => 7788,
                    _ => 7780
                };
            }

            return 7780;
        }

        private static bool TryGetValue(IEnumerable<KeyValuePair<string, object?>> properties,
            string name, out string value)
        {
            foreach (var property in properties)
            {
                if (!string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = Convert.ToString(property.Value) ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
