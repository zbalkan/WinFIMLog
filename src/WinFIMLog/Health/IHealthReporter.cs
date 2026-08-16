namespace WinFIMLog.Health
{
    public interface IHealthReporter
    {
        void CoverageGap(string source, string scope, string reason, long lostCount = 1);
        void SourceRecovered(string source, string scope, string action);
        void SinkFailure(string sink, string reason, int attempt);
        void ConfigurationChanged(string previousScopeHash, string newScopeHash) { }
    }
}
