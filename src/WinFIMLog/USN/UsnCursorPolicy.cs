using WinFIMLog.Snapshots;

namespace WinFIMLog.USN
{
    /// <summary>Why a read is starting where it is starting.</summary>
    internal enum UsnCursorAction
    {
        /// <summary>A stored cursor is still inside the journal's retained range.</summary>
        Resume,

        /// <summary>No cursor exists for this volume, so retained history is read once.</summary>
        FirstRun,

        /// <summary>The journal was deleted and recreated; the stored cursor means nothing.</summary>
        JournalWrap,

        /// <summary>The stored cursor fell out of the retained range while the service was away.</summary>
        CursorAgeOut
    }

    /// <summary>Where a volume read should start, and whether that start implies a coverage gap.</summary>
    internal readonly record struct UsnCursorDecision(UsnCursorAction Action, long StartUsn)
    {
        /// <summary>True when records between the stored cursor and the start point were lost.</summary>
        public bool IsGap => Action is UsnCursorAction.JournalWrap or UsnCursorAction.CursorAgeOut;

        /// <summary>Stable reason string reported with the coverage gap.</summary>
        public string Reason => Action.ToString();
    }

    /// <summary>
    /// Decides where to resume reading a volume's journal, isolated from the P/Invoke surface so the
    /// state machine is testable without a Windows volume.
    /// </summary>
    /// <remarks>
    /// The journal is a ring. Three things can invalidate a stored cursor and each is reported rather
    /// than absorbed, per ADR-0003: the journal being recreated (a new journal id), the cursor ageing
    /// out below the retained range, and the absence of any cursor at all. Only the first two are
    /// gaps; a first run reads whatever history the ring still holds and loses nothing that was ever
    /// observable to this service.
    /// </remarks>
    internal static class UsnCursorPolicy
    {
        public static UsnCursorDecision Decide(UsnJournalCursor? cursor, ulong journalId,
            long firstUsn, long lowestValidUsn, long nextUsn)
        {
            // The retained range floor. FirstUsn can lag LowestValidUsn after journal trimming, so
            // the higher of the two is the earliest position a read can legitimately start from.
            var floor = lowestValidUsn > firstUsn ? lowestValidUsn : firstUsn;

            if (cursor is null || !cursor.IsValid)
            {
                return new UsnCursorDecision(UsnCursorAction.FirstRun, floor);
            }

            if (cursor.JournalId != journalId)
            {
                return new UsnCursorDecision(UsnCursorAction.JournalWrap, floor);
            }

            if (cursor.LastReadUsn < floor)
            {
                return new UsnCursorDecision(UsnCursorAction.CursorAgeOut, floor);
            }

            // A cursor ahead of NextUsn means the journal moved backwards under us; treating it as an
            // age-out re-reads from the floor rather than silently skipping the whole retained range.
            if (cursor.LastReadUsn > nextUsn)
            {
                return new UsnCursorDecision(UsnCursorAction.CursorAgeOut, floor);
            }

            return new UsnCursorDecision(UsnCursorAction.Resume, cursor.LastReadUsn);
        }
    }
}
