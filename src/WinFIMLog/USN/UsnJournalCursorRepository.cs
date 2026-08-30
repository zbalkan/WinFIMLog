using System;
using System.Linq;
using WinFIMLog.Data;
using WinFIMLog.Snapshots;

namespace WinFIMLog.USN
{
    /// <summary>Persists journal read positions and the gaps that invalidated them.</summary>
    /// <remarks>
    /// The cursor is what makes the journal source survive a restart: without it every start would
    /// either re-read the whole retained ring or skip the downtime window entirely. Gaps are stored
    /// rather than only reported so an operator can answer "what was not covered, and when" after
    /// the fact, which ADR-0003 requires of any loss.
    /// </remarks>
    public sealed class UsnJournalCursorRepository
    {
        private readonly ILiteDbContext context;

        public UsnJournalCursorRepository(ILiteDbContext context) => this.context = context;

        /// <summary>Builds the single indexed identity for a volume.</summary>
        public static string VolumeKey(string volumeSerialNumber, char driveLetter) =>
            $"{volumeSerialNumber}|{char.ToUpperInvariant(driveLetter)}";

        public UsnJournalCursor? Find(string volumeKey) =>
            context.UsnJournalCursors.Query().Where(cursor => cursor.VolumeKey == volumeKey).FirstOrDefault();

        /// <summary>Writes the read position reached for a volume.</summary>
        public void Save(string volumeKey, string volumeSerialNumber, char driveLetter,
            ulong journalId, long lastReadUsn)
        {
            var cursor = Find(volumeKey) ?? new UsnJournalCursor { VolumeKey = volumeKey };

            cursor.VolumeSerialNumber = volumeSerialNumber;
            cursor.DriveLetter = char.ToUpperInvariant(driveLetter);
            cursor.JournalId = journalId;
            cursor.LastReadUsn = lastReadUsn;
            cursor.LastUpdated = DateTimeOffset.UtcNow;
            cursor.ConsecutiveReadFailures = 0;
            cursor.IsValid = true;

            context.UsnJournalCursors.Upsert(cursor);
        }

        /// <summary>Marks a cursor unusable so the next successful open restarts from the floor.</summary>
        public void Invalidate(string volumeKey)
        {
            var cursor = Find(volumeKey);
            if (cursor is null)
            {
                return;
            }

            cursor.IsValid = false;
            cursor.ConsecutiveReadFailures++;
            cursor.LastUpdated = DateTimeOffset.UtcNow;
            context.UsnJournalCursors.Upsert(cursor);
        }

        /// <summary>Records a coverage gap and the position reading resumed from.</summary>
        public UsnJournalGap RecordGap(string volumeKey, string volumeSerialNumber, char driveLetter,
            string reason, long? lastReadUsn, long resumeFromUsn)
        {
            var gap = new UsnJournalGap
            {
                VolumeKey = volumeKey,
                VolumeSerialNumber = volumeSerialNumber,
                DriveLetter = char.ToUpperInvariant(driveLetter),
                Reason = reason,
                StartUsnMissing = lastReadUsn,
                EndUsnRecovered = lastReadUsn,
                ResumeFromUsn = resumeFromUsn,
                DetectedAt = DateTimeOffset.UtcNow,

                // The journal exposes positions, not record counts, so the span is reported in USN
                // units rather than an invented record estimate.
                EstimatedRecordsLost = 0
            };

            context.UsnJournalGaps.Insert(gap);

            var cursor = Find(volumeKey);
            if (cursor is not null)
            {
                cursor.GapsDetected++;
                cursor.LastGapDetectedAt = gap.DetectedAt;
                context.UsnJournalCursors.Upsert(cursor);
            }

            return gap;
        }
    }
}
