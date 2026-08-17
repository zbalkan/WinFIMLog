namespace WinFIMLog.Health
{
    public interface IHealthReporter
    {
        void ConfigurationChanged(string previousScopeHash, string newScopeHash) { }

        void CoverageGap(string source, string scope, string reason, long lostCount = 1);

        void Heartbeat(HealthMetrics metrics) { }

        void SinkFailure(string sink, string reason, int attempt);

        void SourceRecovered(string source, string scope, string action);
    }
}
