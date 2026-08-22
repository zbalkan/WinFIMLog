namespace WinFIMLog.FIM
{
    public enum AttributionStatus
    {
        Unattributed,
        Attributed,
        Unavailable,

        /// <summary>
        /// The source could not supply the mandatory process rundown.
        /// </summary>
        RundownMissing,

        /// <summary>
        /// A subject exists, but thread impersonation makes process identity ambiguous.
        /// </summary>
        ImpersonationAmbiguous,
    }
}
