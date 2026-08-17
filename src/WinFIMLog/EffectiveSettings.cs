using System;
using System.Text.RegularExpressions;
using WinFIMLog.Configuration;

namespace WinFIMLog
{
    /// <summary>An immutable-by-public-contract configuration generation.</summary>
    public sealed class EffectiveSettings
    {
        internal int CaptureQueueCapacity { get; set; } = 8192;
        internal bool EnableLocalDatabase { get; set; } = true;
        internal bool EnableRegistryMonitoring { get; set; }
        internal string[] ExcludedExtensions { get; set; } = Array.Empty<string>();
        internal Regex? ExcludedExtensionsPattern { get; set; }
        internal string[] ExcludedKeys { get; set; } = Array.Empty<string>();
        internal Regex? ExcludedKeysPattern { get; set; }
        internal string[] ExcludedPaths { get; set; } = Array.Empty<string>();
        internal Regex? ExcludedPathsPattern { get; set; }
        internal int FileSystemSnapshotInterval { get; set; } = 21600;
        internal int HashLimitMB { get; set; }
        internal int HeartbeatInterval { get; set; }
        internal string[] MonitoredKeys { get; set; } = Array.Empty<string>();
        internal Regex MonitoredKeysPattern { get; set; } = null!;
        internal string[] MonitoredPaths { get; set; } = Array.Empty<string>();
        internal Regex MonitoredPathsPattern { get; set; } = null!;
        internal RegistryScopeMatcher RegistryScopeMatcher { get; set; } = null!;
        internal int RegistrySnapshotInterval { get; set; } = 21600;
        internal string ScopeHash { get; set; } = string.Empty;
        internal int ScopeReresolutionInterval { get; set; } = 300;
        internal int WatcherBufferSizeKB { get; set; } = 64;

        public bool IsMonitoredKey(string keyName) => RegistryScopeMatcher.IsMatch(keyName);

        public bool IsMonitoredPath(string path) => MonitoredPathsPattern.IsMatch(path) &&
                    !(ExcludedPathsPattern?.IsMatch(path) ?? false) &&
            !(ExcludedExtensionsPattern?.IsMatch(path) ?? false);
    }
}
