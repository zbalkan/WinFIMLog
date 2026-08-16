using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using WinFIMLog.Events;
using WinFIMLog.IO;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Health
{
    internal sealed class HealthReporter(ILogger<HealthReporter> logger, Settings settings,
        ILocalEventSink eventSink, SnapshotHealthState snapshots, EventOutboxRepository outbox) : IHealthReporter
    {
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1) =>
            Write(HealthEventId.CoverageGap, "CoverageGap", new Dictionary<string, object?>
            { ["source"] = source, ["scope"] = scope, ["reason"] = reason, ["lostCount"] = lostCount }, true);

        public void SourceRecovered(string source, string scope, string action) =>
            Write(HealthEventId.SourceRecovered, "SourceRecovered", new Dictionary<string, object?>
            { ["source"] = source, ["scope"] = scope, ["action"] = action });

        public void SinkFailure(string sink, string reason, int attempt) =>
            Write(HealthEventId.SinkFailure, "SinkFailure", new Dictionary<string, object?>
            { ["sink"] = sink, ["reason"] = reason, ["attempt"] = attempt }, true);

        public void ConfigurationChanged(string previousScopeHash, string newScopeHash) =>
            Write(HealthEventId.ConfigurationChanged, "ConfigurationChanged", new Dictionary<string, object?>
            { ["previousScopeHash"] = previousScopeHash, ["newScopeHash"] = newScopeHash });

        public void Heartbeat(HealthMetrics metrics) =>
            Write(HealthEventId.Heartbeat, "Health", new Dictionary<string, object?>
            {
                ["queueDepth"] = metrics.QueueDepth,
                ["oldestItemAgeMs"] = metrics.OldestItemAge.TotalMilliseconds,
                ["accepted"] = metrics.Accepted,
                ["processed"] = metrics.Processed,
                ["dropped"] = metrics.Dropped,
                ["enrichmentFailures"] = metrics.EnrichmentFailures
                , ["fileSystemSnapshotRunning"] = snapshots.FileSystemRunning
                , ["fileSystemSnapshotFailures"] = snapshots.FileSystemFailures
                , ["fileSystemSnapshotLastSuccess"] = snapshots.FileSystemLastSuccess
                , ["fileSystemSnapshotLastDurationMs"] = snapshots.FileSystemLastDuration.TotalMilliseconds
                , ["registrySnapshotRunning"] = snapshots.RegistryRunning
                , ["registrySnapshotFailures"] = snapshots.RegistryFailures
                , ["registrySnapshotLastSuccess"] = snapshots.RegistryLastSuccess
                , ["registrySnapshotLastDurationMs"] = snapshots.RegistryLastDuration.TotalMilliseconds
                , ["eventOutboxPending"] = outbox.PendingCount
                , ["eventOutboxOldestAgeMs"] = outbox.OldestPending is { } oldest
                    ? (DateTimeOffset.UtcNow - oldest).TotalMilliseconds : 0
                , ["databaseBytes"] = DatabaseBytes()
                , ["databaseVolumeFreeBytes"] = DatabaseVolumeFreeBytes()
            });

        private long? DatabaseBytes()
        { try { return new FileInfo(settings.DatabasePath).Exists ? new FileInfo(settings.DatabasePath).Length : 0; } catch { return null; } }

        private long? DatabaseVolumeFreeBytes()
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(settings.DatabasePath));
                return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return null; }
        }

        private void Write(HealthEventId id, string type, IReadOnlyDictionary<string, object?> fields, bool error = false)
        {
            var record = EventContract.Create((ushort)id, type, Guid.NewGuid().ToString("N"), settings.ScopeHash, fields);
            try { eventSink.Write(record, error); }
            catch (Exception exception)
            {
                // The reporting sink itself may be the failed component. Never recurse through
                // SinkFailure; retain a last-resort Application-log/console diagnostic instead.
                logger.LogError(exception, "Structured health record {RecordType} ({EventId}) could not be written", type, (ushort)id);
            }
        }
    }
}
