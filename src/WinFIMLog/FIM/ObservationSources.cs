namespace WinFIMLog.FIM
{
    /// <summary>Stable source names carried on a change and emitted in the event contract.</summary>
    public static class ObservationSources
    {
        /// <summary>Tier 1 notification source. Carries best-effort process attribution.</summary>
        public const string FileSystemWatcher = "FileSystemWatcher";

        /// <summary>Tier 0.5 NTFS change journal. Never carries attribution.</summary>
        public const string UsnJournal = "UsnJournal";

        /// <summary>Tier 0 snapshot reconciliation. The completeness authority.</summary>
        public const string Snapshot = "Snapshot";
    }
}
