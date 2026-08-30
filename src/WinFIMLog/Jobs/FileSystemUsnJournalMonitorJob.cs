using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using WinFIMLog.Snapshots;
using WinFIMLog.USN;

namespace WinFIMLog.Jobs
{
    /// <summary>
    /// Tier 0.5 source: polls each monitored NTFS volume's change journal for activity the
    /// FileSystemWatcher path did not report.
    /// </summary>
    /// <remarks>
    /// This exists for one class of finding that neither other tier can produce. A snapshot cannot
    /// see an object that was created and deleted between scans, because it is gone by scan time,
    /// and a watcher notification for it is lost outright if the watcher overflowed or the service
    /// was not running. NTFS appends the journal record as part of the transaction, so the record
    /// survives both.
    ///
    /// It is deliberately subordinate. Journal records carry no hash, no ACL and no process, so a
    /// record that Tier 1 also saw is dropped in favour of the attributed Tier 1 event, and nothing
    /// here ever advances or completes a Tier 0 baseline.
    /// </remarks>
    internal sealed class FileSystemUsnJournalMonitorJob : IMonitor
    {
        /// <summary>How long a record must have settled before Tier 1 loses its claim on it.</summary>
        private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

        private const string SourceName = "UsnJournal";

        private readonly UsnCorrelationTracker _correlation;
        private readonly IHealthReporter _health;
        private readonly ILogger _logger;
        private readonly IBuffer<FileSystemChange> _output;
        private readonly Dictionary<char, UsnJournalReader> _readers = new();
        private readonly UsnJournalCursorRepository _repository;
        private readonly Settings _settings;
        private readonly ISnapshotCoordinator _snapshots;
        private readonly HashSet<char> _unavailableVolumes = [];
        private bool _disposed;

        public FileSystemUsnJournalMonitorJob(ILogger logger, IBuffer<FileSystemChange> output,
            UsnJournalCursorRepository repository, UsnCorrelationTracker correlation,
            Settings settings, IHealthReporter health, ISnapshotCoordinator snapshots)
        {
            _logger = logger;
            _output = output;
            _repository = repository;
            _correlation = correlation;
            _settings = settings;
            _health = health;
            _snapshots = snapshots;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(_settings.UsnJournalPollIntervalSeconds);
            _logger.LogInformation("USN journal monitoring starting with a {Interval}s poll interval",
                _settings.UsnJournalPollIntervalSeconds);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await PollAsync(cancellationToken);
                    _correlation.Prune(DateTimeOffset.UtcNow);
                    await Task.Delay(interval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal source shutdown.
            }
            finally
            {
                DisposeReaders();
            }
        }

        /// <summary>Runs one poll across every monitored NTFS volume.</summary>
        internal async Task PollAsync(CancellationToken cancellationToken)
        {
            var configuration = _settings.Capture();
            var settleThreshold = DateTime.UtcNow - SettleDelay;

            foreach (var driveLetter in MonitoredVolumes(configuration))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await PollVolumeAsync(driveLetter, configuration, settleThreshold);
                }
                catch (Exception exception)
                {
                    // One unhealthy volume must not stop the others from being read.
                    _logger.LogError(exception, "USN journal poll failed for volume {Drive}:", driveLetter);
                }
            }
        }

        private async Task PollVolumeAsync(char driveLetter, EffectiveSettings configuration,
            DateTime settleThreshold)
        {
            var reader = ResolveReader(driveLetter);
            if (reader is null)
            {
                return;
            }

            if (!reader.TryQueryJournal(out var journal))
            {
                ReportVolumeUnavailable(driveLetter, "JournalQueryFailed");
                DropReader(driveLetter);
                return;
            }

            var volumeKey = UsnJournalCursorRepository.VolumeKey(reader.VolumeSerialNumber, driveLetter);
            var stored = _repository.Find(volumeKey);
            var decision = UsnCursorPolicy.Decide(stored, journal.UsnJournalID,
                journal.FirstUsn, journal.LowestValidUsn, journal.NextUsn);

            var result = reader.Read(decision.StartUsn, settleThreshold);
            if (result.Status != UsnReadStatus.Succeeded)
            {
                ReportVolumeUnavailable(driveLetter, result.Status.ToString());
                if (result.Status == UsnReadStatus.JournalUnavailable)
                {
                    DropReader(driveLetter);
                }

                return;
            }

            if (decision.IsGap)
            {
                // Reported only once the read that recovers from it has succeeded. Reporting on the
                // decision alone would re-emit the same gap on every poll while a volume's reads keep
                // failing, because the cursor that resolves it is only saved after a successful read.
                //
                // Loss is reported and reconciled, never absorbed (ADR-0003). The journal cannot say
                // what was in the lost span, so a Tier 0 snapshot resolves persistent state.
                _repository.RecordGap(volumeKey, reader.VolumeSerialNumber, driveLetter,
                    decision.Reason, stored?.LastReadUsn, decision.StartUsn);
                _health.CoverageGap(SourceName, $"{driveLetter}:", decision.Reason);
                _snapshots.RequestFileSystemSnapshot($"UsnJournal{decision.Reason}", $"{driveLetter}:");
                _logger.LogWarning("USN journal coverage gap on {Drive}: ({Reason}); resuming at USN {Usn}",
                    driveLetter, decision.Reason, decision.StartUsn);
            }

            var published = 0;
            foreach (var record in result.Records)
            {
                var change = UsnChangeMapper.Map(record, reader.PathCache!, configuration, driveLetter);
                if (change is null)
                {
                    continue;
                }

                if (!_correlation.ShouldPublish(change.FullPath, change.ChangeCategory, change.DateTime))
                {
                    continue;
                }

                await _output.Add(change);
                published++;
            }

            _repository.Save(volumeKey, reader.VolumeSerialNumber, driveLetter,
                journal.UsnJournalID, result.NextUsn);

            if (published > 0)
            {
                _logger.LogDebug(
                    "USN journal published {Published} of {Read} records on {Drive}: ({Deferred} deferred)",
                    published, result.Records.Count, driveLetter, result.DeferredCount);
            }
        }

        /// <summary>NTFS volumes that hold at least one monitored path.</summary>
        internal static IReadOnlyList<char> MonitoredVolumes(EffectiveSettings configuration)
        {
            var roots = new HashSet<char>();
            foreach (var path in configuration.MonitoredPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
                    char.IsAsciiLetter(path[0]) && path.Length > 1 && path[1] == ':')
                {
                    roots.Add(char.ToUpperInvariant(path[0]));
                }
            }

            if (roots.Count == 0)
            {
                return [];
            }

            // A journal exists only on NTFS. Other formats are skipped rather than retried, and the
            // absence of coverage there is a stated limitation rather than a runtime failure.
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady &&
                                string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) &&
                                drive.Name.Length > 0 &&
                                roots.Contains(char.ToUpperInvariant(drive.Name[0])))
                .Select(drive => char.ToUpperInvariant(drive.Name[0]))
                .Distinct()
                .Order()
                .ToArray();
        }

        private UsnJournalReader? ResolveReader(char driveLetter)
        {
            if (_readers.TryGetValue(driveLetter, out var existing))
            {
                return existing;
            }

            var reader = new UsnJournalReader(driveLetter, _logger);
            if (!reader.TryOpen())
            {
                reader.Dispose();
                ReportVolumeUnavailable(driveLetter, "VolumeOpenFailed");
                return null;
            }

            _readers[driveLetter] = reader;
            if (_unavailableVolumes.Remove(driveLetter))
            {
                _health.SourceRecovered(SourceName, $"{driveLetter}:", "VolumeReopened");
            }

            return reader;
        }

        /// <summary>Reports a volume as uncovered once, not on every poll.</summary>
        private void ReportVolumeUnavailable(char driveLetter, string reason)
        {
            if (!_unavailableVolumes.Add(driveLetter))
            {
                return;
            }

            _health.CoverageGap(SourceName, $"{driveLetter}:", reason);
            _logger.LogWarning("USN journal coverage unavailable on {Drive}: ({Reason})", driveLetter, reason);
        }

        private void DropReader(char driveLetter)
        {
            if (_readers.Remove(driveLetter, out var reader))
            {
                reader.Dispose();
            }
        }

        private void DisposeReaders()
        {
            foreach (var reader in _readers.Values)
            {
                reader.Dispose();
            }

            _readers.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeReaders();
        }
    }
}
