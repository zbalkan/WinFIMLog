using System;

namespace WinFIMLog.Snapshots
{
    /// <summary>
    /// Caches whether watcher notifications have an authoritative filesystem baseline to
    /// compare against.  The watcher callback must not query the embedded database.
    /// </summary>
    public sealed class FileSystemBaselineAvailability
    {
        private readonly BaselineRepository repository;
        private readonly object sync = new();
        private string establishedScope = string.Empty;
        private string establishedIdentity = string.Empty;

        public FileSystemBaselineAvailability(BaselineRepository repository, Settings settings)
        {
            this.repository = repository;
            Refresh(settings.Capture());
        }

        public bool IsEstablished(EffectiveSettings configuration)
        {
            var identity = SourceIdentityProvider.FileSystem(configuration.MonitoredPaths);
            lock (sync)
                return string.Equals(establishedScope, configuration.ScopeHash, StringComparison.Ordinal) &&
                    string.Equals(establishedIdentity, identity, StringComparison.Ordinal);
        }

        public void Refresh(EffectiveSettings configuration)
        {
            var identity = SourceIdentityProvider.FileSystem(configuration.MonitoredPaths);
            var complete = repository.LatestComplete(BaselineSource.FileSystem,
                configuration.ScopeHash, identity) is not null;
            lock (sync)
            {
                establishedScope = complete ? configuration.ScopeHash : string.Empty;
                establishedIdentity = complete ? identity : string.Empty;
            }
        }
    }
}
