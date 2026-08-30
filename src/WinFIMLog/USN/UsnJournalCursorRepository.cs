using System;
using WinFIMLog.Data;
using WinFIMLog.Snapshots;

namespace WinFIMLog.USN
{
    /// <summary>Persists where journal replay resumes for each volume.</summary>
    /// <remarks>
    /// Without a persisted position every service start would either re-read the whole retained ring
    /// or skip the downtime window entirely. Loss itself is reported through
    /// <see cref="Health.IHealthReporter.CoverageGap"/> rather than stored here.
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

        /// <summary>Writes the position replay reached for a volume.</summary>
        public void Save(string volumeKey, ulong journalId, long lastReadUsn)
        {
            var cursor = Find(volumeKey) ?? new UsnJournalCursor { VolumeKey = volumeKey };
            cursor.JournalId = journalId;
            cursor.LastReadUsn = lastReadUsn;
            cursor.LastUpdated = DateTimeOffset.UtcNow;
            cursor.IsValid = true;
            context.UsnJournalCursors.Upsert(cursor);
        }

    }
}
