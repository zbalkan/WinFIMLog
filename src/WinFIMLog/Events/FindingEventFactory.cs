using System;
using System.Collections.Generic;
using WinFIMLog.FIM;

namespace WinFIMLog.Events
{
    internal static class FindingEventFactory
    {
        internal static EventContract FileSystem(FileSystemChange change)
        {
            var eventId = change.ChangeCategory switch
            {
                ChangeCategory.Created => EventIdCatalog.FileSystemCreated,
                ChangeCategory.Changed => EventIdCatalog.FileSystemChanged,
                ChangeCategory.Deleted => EventIdCatalog.FileSystemDeleted,
                _ => throw new ArgumentOutOfRangeException(nameof(change), "Discovery is not an Event Log finding.")
            };
            return EventContract.Create(eventId, "FileSystemFinding", change.Id, change.ScopeHash,
                new Dictionary<string, object?>
                {
                    ["category"] = change.ChangeCategory.ToString(),
                    ["operation"] = FileSystemOperation(change),
                    ["path"] = change.Entity,
                    ["oldPath"] = change.OldPath,
                    ["newPath"] = change.NewPath,
                    ["currentHash"] = change.CurrentHash,
                    ["previousHash"] = change.PreviousHash,
                    ["currentSizeBytes"] = change.CurrentSizeBytes,
                    ["previousSizeBytes"] = change.PreviousSizeBytes,
                    ["currentAcl"] = change.ACLs,
                    ["previousAcl"] = change.PreviousACL,
                    ["objectType"] = change.ObjectType.ToString(),

                    // Which tier saw this. A UsnJournal finding is namespace evidence only, so a
                    // consumer must not read its absent hash or attribution as a failed lookup.
                    ["observationSource"] = change.ObservationSource,
                    ["usn"] = change.UsnValue,
                    ["usnReason"] = change.UsnReason,
                    ["pathUnresolved"] = change.PathUnresolved ? true : null,
                    ["renameCorrelationMethod"] = change.OldPath is not null ? "RuntimeAdjacentBufferPair" : null,
                    ["renameCorrelationConfidence"] = change.OldPath is not null ? "Low" : null,
                    ["attributionStatus"] = change.AttributionStatus.ToString(),
                    ["attributionMethod"] = change.AttributionMethod,
                    ["attributionConfidence"] = change.AttributionConfidence,
                    ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp,
                    ["attributionMissingReason"] = change.AttributionMissingReason,
                    ["processSequenceNumber"] = change.ProcessSequenceNumber,
                    ["processId"] = change.ProcessID,
                    ["processName"] = change.ProcessName,
                    ["userSid"] = change.UserSID,
                    ["username"] = change.Username
                });
        }

        internal static EventContract Registry(RegistryChange change)
        {
            var eventId = change.ChangeCategory switch
            {
                ChangeCategory.Created => EventIdCatalog.RegistryCreated,
                ChangeCategory.Changed => EventIdCatalog.RegistryChanged,
                ChangeCategory.Deleted => EventIdCatalog.RegistryDeleted,
                _ => throw new ArgumentOutOfRangeException(nameof(change), "Unsupported Registry change category.")
            };
            return EventContract.Create(eventId, "RegistryFinding", change.Id, change.ScopeHash,
                new Dictionary<string, object?>
                {
                    ["category"] = change.ChangeCategory.ToString(),
                    ["operation"] = change.ChangeCategory switch
                    {
                        ChangeCategory.Created => "Created",
                        ChangeCategory.Changed => "Modified",
                        ChangeCategory.Deleted => "Deleted",
                        _ => "Other"
                    },
                    ["key"] = change.Entity,
                    ["hive"] = change.Hive,
                    ["valueName"] = change.ValueName,
                    ["valueData"] = change.ValueData,
                    ["evidenceStatus"] = change.EvidenceStatus,
                    ["evidenceMissingReason"] = change.EvidenceMissingReason,
                    ["currentAcl"] = change.ACLs,
                    ["previousAcl"] = change.PreviousACL,
                    ["attributionStatus"] = change.AttributionStatus.ToString(),
                    ["attributionMethod"] = change.AttributionMethod,
                    ["attributionConfidence"] = change.AttributionConfidence,
                    ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp,
                    ["attributionMissingReason"] = change.AttributionMissingReason,
                    ["processId"] = change.ProcessID,
                    ["processName"] = change.ProcessName,
                    ["userSid"] = change.UserSID,
                    ["username"] = change.Username
                });
        }

        private static string FileSystemOperation(FileSystemChange change) =>
            change.OldPath is not null && change.NewPath is not null
                ? "RenamedOrMoved"
                : change.ChangeCategory switch
                {
                    ChangeCategory.Created => "Created",
                    ChangeCategory.Changed => "Modified",
                    ChangeCategory.Deleted => "Deleted",
                    _ => "Other"
                };
    }
}
