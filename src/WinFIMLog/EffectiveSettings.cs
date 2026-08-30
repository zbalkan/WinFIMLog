using System.Text.RegularExpressions;
using WinFIMLog.Configuration;
using WinFIMLog.Snapshots;

namespace WinFIMLog
{
    /// <summary>An immutable-by-public-contract configuration generation.</summary>
    public sealed class EffectiveSettings
    {
        internal int CaptureQueueCapacity { get; set; } = 8192;
        internal int DiscoveryConcurrency { get; set; } = 2;
        internal bool EnableLocalDatabase { get; set; } = true;
        internal bool EnableRegistryMonitoring { get; set; }

        /// <summary>Tier 0.5 NTFS change journal source. Opt-in until the ADR-0016 gate is recorded.</summary>
        internal bool EnableUsnJournalMonitoring { get; set; }

        internal int UsnJournalPollIntervalSeconds { get; set; } = 6;

        /// <summary>How long a watcher observation suppresses a matching journal record.</summary>
        internal int UsnCorrelationWindowSeconds { get; set; } = 30;
        internal TpmIntegrityMode TpmIntegrityMode { get; set; }
        internal byte[] TpmIntegrityPublicKey { get; set; } = [];
        internal string[] ExcludedExtensions { get; set; } = [];
        internal Regex? ExcludedExtensionsPattern { get; set; }
        internal string[] ExcludedKeys { get; set; } = [];
        internal Regex? ExcludedKeysPattern { get; set; }
        internal string[] ExcludedPaths { get; set; } = [];
        internal Regex? ExcludedPathsPattern { get; set; }
        internal int FileSystemSnapshotInterval { get; set; } = 21600;
        internal int HashLimitMB { get; set; }
        internal int HeartbeatInterval { get; set; }
        internal string[] MonitoredKeys { get; set; } = [];
        internal Regex MonitoredKeysPattern { get; set; } = null!;
        internal string[] MonitoredPaths { get; set; } = [];
        internal Regex MonitoredPathsPattern { get; set; } = null!;
        internal RegistryScopeMatcher RegistryScopeMatcher { get; set; } = null!;
        internal int RegistrySnapshotInterval { get; set; } = 21600;
        internal string ScopeHash { get; set; } = string.Empty;
        internal int ScopeReresolutionInterval { get; set; } = 30;
        internal int WatcherBufferSizeKB { get; set; } = 64;

        public bool IsMonitoredKey(string keyName) => RegistryScopeMatcher.IsMatch(keyName);

        public bool IsMonitoredPath(string path) =>
            PathScopeMatcher.IsWithinAny(MonitoredPaths, path) &&
            !PathScopeMatcher.IsWithinAny(ExcludedPaths, path) &&
            !(ExcludedExtensionsPattern?.IsMatch(path) ?? false);
    }
}
