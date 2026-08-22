using WinFIMLog.Snapshots;

namespace WinFIMLog.Health
{
    public interface IHealthReporter
    {
        void ConfigurationChanged(string previousScopeHash, string newScopeHash) { }

        void CoverageGap(string source, string scope, string reason, long lostCount = 1);

        void Heartbeat(HealthMetrics metrics) { }

        void SinkFailure(string sink, string reason, int attempt);

        void TpmIntegrityUnavailable(string scope, string reason,
            BaselineAlgorithm fallbackAlgorithm = BaselineAlgorithm.Sha256) { }

        void SourceRecovered(string source, string scope, string action);
    }
}
