using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WinFIMLog.Events
{
    /// <summary>Stable Windows Event Log allocations for structured records.</summary>
    internal static class EventIdCatalog
    {
        internal const ushort ServiceError = 7770;
        internal const ushort FileSystemCreated = 7776;
        internal const ushort FileSystemChanged = 7777;
        internal const ushort FileSystemDeleted = 7778;
        internal const ushort OtherServiceEvent = 7780;
        internal const ushort RegistryCreated = 7786;
        internal const ushort RegistryChanged = 7787;
        internal const ushort RegistryDeleted = 7788;
        internal const ushort Health = 7790;
        internal const ushort CoverageGap = 7791;
        internal const ushort SourceRecovered = 7792;
        internal const ushort SinkFailure = 7793;
        internal const ushort ConfigurationChanged = 7794;
        internal const ushort BaselineFinding = 7795;
        internal const ushort Aggregation = 7796;
        internal const ushort SecurityAuditAttribution = 7797;

        private static readonly IReadOnlyDictionary<ushort, string> RecordTypes =
            new Dictionary<ushort, string>
            {
                [FileSystemCreated] = "FileSystemFinding",
                [FileSystemChanged] = "FileSystemFinding",
                [FileSystemDeleted] = "FileSystemFinding",
                [RegistryCreated] = "RegistryFinding",
                [RegistryChanged] = "RegistryFinding",
                [RegistryDeleted] = "RegistryFinding",
                [Health] = "Health",
                [CoverageGap] = "CoverageGap",
                [SourceRecovered] = "SourceRecovered",
                [SinkFailure] = "SinkFailure",
                [ConfigurationChanged] = "ConfigurationChanged",
                [BaselineFinding] = "BaselineFinding",
                [Aggregation] = "Aggregation",
                [SecurityAuditAttribution] = "SecurityAuditAttribution"
            };

        internal static void Validate(ushort eventId, string recordType, EventChannel channel)
        {
            if (!RecordTypes.TryGetValue(eventId, out var expectedType) ||
                !string.Equals(expectedType, recordType, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Event ID {eventId} is not allocated to record type '{recordType}'.");
            }

            var expectedChannel = eventId switch
            {
                BaselineFinding => EventChannel.Baseline,
                SecurityAuditAttribution => EventChannel.Diagnostic,
                _ => EventChannel.Operational
            };
            if (channel != expectedChannel)
            {
                throw new ArgumentException($"Event ID {eventId} must be written to the {expectedChannel} channel.");
            }
        }

        internal static EventLogEntryType EntryType(ushort eventId, bool errorFallback = false) => eventId switch
        {
            CoverageGap or SinkFailure or ServiceError => EventLogEntryType.Error,
            ConfigurationChanged or BaselineFinding => EventLogEntryType.Warning,
            _ when errorFallback => EventLogEntryType.Error,
            _ => EventLogEntryType.Information
        };
    }
}
