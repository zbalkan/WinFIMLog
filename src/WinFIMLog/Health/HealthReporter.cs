using Microsoft.Extensions.Logging;

namespace WinFIMLog.Health
{
    internal sealed class HealthReporter(ILogger<HealthReporter> logger, Settings settings) : IHealthReporter
    {
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1) =>
            logger.LogError((int)HealthEventId.CoverageGap, "COVERAGE GAP Source={Source} Scope={Scope} ScopeHash={ScopeHash} Reason={Reason} LostCount={LostCount}", source, scope, settings.ScopeHash, reason, lostCount);

        public void SourceRecovered(string source, string scope, string action) =>
            logger.LogInformation((int)HealthEventId.SourceRecovered, "SOURCE RECOVERED Source={Source} Scope={Scope} ScopeHash={ScopeHash} Action={Action}", source, scope, settings.ScopeHash, action);

        public void SinkFailure(string sink, string reason, int attempt) =>
            logger.LogError((int)HealthEventId.SinkFailure, "SINK FAILURE Sink={Sink} ScopeHash={ScopeHash} Reason={Reason} Attempt={Attempt}", sink, settings.ScopeHash, reason, attempt);

        public void ConfigurationChanged(string previousScopeHash, string newScopeHash) =>
            logger.LogWarning((int)HealthEventId.ConfigurationChanged,
                "CONFIGURATION CHANGED PreviousScopeHash={PreviousScopeHash} NewScopeHash={NewScopeHash}", previousScopeHash, newScopeHash);
    }
}
