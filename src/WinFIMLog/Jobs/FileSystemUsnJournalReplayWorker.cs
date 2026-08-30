using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using WinFIMLog.USN;

namespace WinFIMLog.Jobs
{
    /// <summary>
    /// Tier 0.5: replays the NTFS change journal across windows where Tier 1 coverage was lost.
    /// </summary>
    /// <remarks>
    /// FileSystemWatcher already observes transient create-delete pairs while it is healthy, so a
    /// continuously-polled journal would mostly reproduce what Tier 1 reported. The uncovered
    /// windows are the ones the health system already names: watcher failure, capture-queue
    /// shedding, and the downtime between the persisted cursor and service start. This worker reads
    /// the journal for exactly those, and is otherwise idle — it opens no volume handle at all.
    ///
    /// It stays subordinate to Tier 0. Journal records carry no hash, ACL or attribution once the
    /// object is gone, so nothing here advances or completes a baseline.
    ///
    /// A replay can re-report an operation Tier 1 also reported, at the boundary of a gap. That
    /// duplicate is accepted: consumers already deduplicate by RecordId under ADR-0008, and
    /// suppressing it would cost a correlation index larger than this worker.
    /// </remarks>
    internal sealed class FileSystemUsnJournalReplayWorker : BackgroundService
    {
        /// <summary>Caps one replay so a long-retained journal cannot monopolise the worker.</summary>
        private const int MaxRecordsPerReplay = 200_000;

        private const string SourceName = "UsnJournal";

        private readonly UsnReplayCoordinator _coordinator;
        private readonly IHealthReporter _health;
        private readonly ILogger<FileSystemUsnJournalReplayWorker> _logger;
        private readonly IBuffer<FileSystemChange> _output;
        private readonly UsnJournalCursorRepository _repository;
        private readonly Settings _settings;

        public FileSystemUsnJournalReplayWorker(UsnReplayCoordinator coordinator,
            IBuffer<FileSystemChange> output, UsnJournalCursorRepository repository,
            Settings settings, IHealthReporter health,
            ILogger<FileSystemUsnJournalReplayWorker> logger)
        {
            _coordinator = coordinator;
            _output = output;
            _repository = repository;
            _settings = settings;
            _health = health;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Service start is itself a gap: nothing observed the window since the last cursor write.
            _coordinator.RequestReplay("ServiceStart");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var request = await _coordinator.ReadAsync(stoppingToken);
                    await ReplayAsync(request, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
        }

        internal async Task ReplayAsync(UsnReplayRequest request, CancellationToken cancellationToken)
        {
            var configuration = _settings.Capture();
            foreach (var driveLetter in MonitoredVolumes(configuration))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await ReplayVolumeAsync(driveLetter, configuration, request.Reason);
                }
                catch (Exception exception)
                {
                    // One unhealthy volume must not stop the others from being replayed.
                    _logger.LogError(exception, "Journal replay failed for volume {Drive}:", driveLetter);
                    _health.CoverageGap(SourceName, $"{driveLetter}:", $"ReplayFailed:{exception.GetType().Name}");
                }
            }
        }

        private async Task ReplayVolumeAsync(char driveLetter, EffectiveSettings configuration, string reason)
        {
            // The handle and its path cache live only for this replay. Holding them open between
            // replays would pin a volume the service is not otherwise reading.
            using var reader = new UsnJournalReader(driveLetter, _logger);
            if (!reader.TryOpen())
            {
                _health.CoverageGap(SourceName, $"{driveLetter}:", "VolumeOpenFailed");
                return;
            }

            if (!reader.TryQueryJournal(out var journal))
            {
                _health.CoverageGap(SourceName, $"{driveLetter}:", "JournalUnavailable");
                return;
            }

            var volumeKey = UsnJournalCursorRepository.VolumeKey(reader.VolumeSerialNumber, driveLetter);
            var stored = _repository.Find(volumeKey);
            var decision = UsnCursorPolicy.Decide(stored, journal.UsnJournalID,
                journal.FirstUsn, journal.LowestValidUsn, journal.NextUsn);

            if (decision.IsGap)
            {
                // Loss is reported and reconciled, never absorbed (ADR-0003). The journal cannot say
                // what was in the span it discarded, so only Tier 0 can settle persistent state.
                _health.CoverageGap(SourceName, $"{driveLetter}:", decision.Reason);
                _logger.LogWarning("Journal position lost on {Drive}: ({Reason}); replaying from USN {Usn}",
                    driveLetter, decision.Reason, decision.StartUsn);
            }

            var cursor = decision.StartUsn;
            var published = 0;
            var read = 0;

            while (read < MaxRecordsPerReplay)
            {
                var result = reader.Read(cursor);
                if (result.Status != UsnReadStatus.Succeeded)
                {
                    _health.CoverageGap(SourceName, $"{driveLetter}:", result.Status.ToString());
                    return;
                }

                if (result.Records.Count == 0)
                {
                    cursor = result.NextUsn;
                    break;
                }

                foreach (var record in result.Records)
                {
                    var change = UsnChangeMapper.Map(record, reader.PathCache!, configuration, driveLetter);
                    if (change is null)
                    {
                        continue;
                    }

                    await _output.Add(change);
                    published++;
                }

                read += result.Records.Count;
                cursor = result.NextUsn;
            }

            if (read >= MaxRecordsPerReplay)
            {
                // The remainder stays unread. Saving the cursor where it stopped means the next
                // replay continues rather than restarting, but this window is not fully covered.
                _health.CoverageGap(SourceName, $"{driveLetter}:", "ReplayRecordCapReached");
            }

            _repository.Save(volumeKey, journal.UsnJournalID, cursor);

            if (published > 0)
            {
                _logger.LogInformation(
                    "Journal replay on {Drive}: published {Published} of {Read} records ({Reason})",
                    driveLetter, published, read, reason);
            }
        }

        /// <summary>NTFS volumes holding at least one monitored path.</summary>
        internal static IReadOnlyList<char> MonitoredVolumes(EffectiveSettings configuration)
        {
            var roots = new HashSet<char>();
            foreach (var path in configuration.MonitoredPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && path.Length > 1 &&
                    char.IsAsciiLetter(path[0]) && path[1] == ':')
                {
                    roots.Add(char.ToUpperInvariant(path[0]));
                }
            }

            if (roots.Count == 0)
            {
                return [];
            }

            // A change journal exists only on NTFS. Other formats are skipped, and their absence of
            // Tier 0.5 coverage is a stated limitation rather than a runtime failure.
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.Name.Length > 0 &&
                                string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase) &&
                                roots.Contains(char.ToUpperInvariant(drive.Name[0])))
                .Select(drive => char.ToUpperInvariant(drive.Name[0]))
                .Distinct()
                .Order()
                .ToArray();
        }
    }
}
