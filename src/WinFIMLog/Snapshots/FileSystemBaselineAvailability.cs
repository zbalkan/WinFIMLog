using System;
using System.Threading;

namespace WinFIMLog.Snapshots
{
    /// <summary>
    /// Caches whether watcher notifications have an authoritative filesystem baseline to
    /// compare against.  The watcher callback must not query the embedded database.
    /// </summary>
    public sealed class FileSystemBaselineAvailability
    {
        private readonly BaselineRepository repository;
        private readonly Lock sync = new();
        private string establishedIdentity = string.Empty;
        private BaselineAlgorithm? establishedAlgorithm;
        private string establishedScope = string.Empty;

        public FileSystemBaselineAvailability(BaselineRepository repository, Settings settings)
        {
            this.repository = repository;
            Refresh(settings.Capture());
        }

        public bool IsEstablished(EffectiveSettings configuration)
        {
            var identity = SourceIdentityProvider.FileSystem(configuration.MonitoredPaths);
            var algorithm = AlgorithmVersion(configuration);
            lock (sync)
            {
                return string.Equals(establishedScope, configuration.ScopeHash, StringComparison.Ordinal) &&
                    string.Equals(establishedIdentity, identity, StringComparison.Ordinal) &&
                    establishedAlgorithm == algorithm;
            }
        }

        public void Refresh(EffectiveSettings configuration)
        {
            var identity = SourceIdentityProvider.FileSystem(configuration.MonitoredPaths);
            var algorithm = AlgorithmVersion(configuration);
            var complete = repository.LatestComplete(BaselineSource.FileSystem,
                configuration.ScopeHash, identity, algorithm: algorithm) is not null;
            lock (sync)
            {
                establishedScope = complete ? configuration.ScopeHash : string.Empty;
                establishedIdentity = complete ? identity : string.Empty;
                establishedAlgorithm = complete ? algorithm : null;
            }
        }

        internal static BaselineAlgorithm AlgorithmVersion(EffectiveSettings configuration) =>
            configuration.EnableVssFileSystemSnapshots ? BaselineAlgorithm.VssMftPerDrive : BaselineAlgorithm.Sha256;
    }
}
