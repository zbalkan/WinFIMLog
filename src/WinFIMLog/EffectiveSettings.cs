using System;
using System.Text.RegularExpressions;
using WinFIMLog.Configuration;

namespace WinFIMLog
{
    /// <summary>An immutable-by-public-contract configuration generation.</summary>
    public sealed class EffectiveSettings
    {
        internal bool EnableLocalDatabase { get; set; } = true;
        internal bool EnableRegistryMonitoring { get; set; }
        internal string[] ExcludedExtensions { get; set; } = Array.Empty<string>();
        internal string[] ExcludedKeys { get; set; } = Array.Empty<string>();
        internal string[] ExcludedPaths { get; set; } = Array.Empty<string>();
        internal int HashLimitMB { get; set; }
        internal int HeartbeatInterval { get; set; }
        internal int CaptureQueueCapacity { get; set; } = 8192;
        internal int WatcherBufferSizeKB { get; set; } = 64;
        internal int ScopeReresolutionInterval { get; set; } = 300;
        internal int FileSystemSnapshotInterval { get; set; } = 21600;
        internal int RegistrySnapshotInterval { get; set; } = 21600;
        internal string ScopeHash { get; set; } = string.Empty;
        internal string[] MonitoredKeys { get; set; } = Array.Empty<string>();
        internal string[] MonitoredPaths { get; set; } = Array.Empty<string>();
        internal Regex? ExcludedExtensionsPattern { get; set; }
        internal Regex? ExcludedKeysPattern { get; set; }
        internal Regex? ExcludedPathsPattern { get; set; }
        internal Regex MonitoredKeysPattern { get; set; } = null!;
        internal Regex MonitoredPathsPattern { get; set; } = null!;
        internal RegistryScopeMatcher RegistryScopeMatcher { get; set; } = null!;

        public bool IsMonitoredPath(string path) => MonitoredPathsPattern.IsMatch(path) &&
            !(ExcludedPathsPattern?.IsMatch(path) ?? false) &&
            !(ExcludedExtensionsPattern?.IsMatch(path) ?? false);

        public bool IsMonitoredKey(string keyName) => RegistryScopeMatcher.IsMatch(keyName);
    }
}
