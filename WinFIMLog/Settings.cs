using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WinFIMLog.IO;
using WinFIMLog.Configuration;

namespace WinFIMLog
{
    public sealed class Settings
    {
        /// <summary>
        ///     Path to LiteDB database file
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">
        /// </exception>
        public string DatabasePath => $"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\\FIM\\fim.db";

        /// <summary>
        ///     Switch to enable/disable local database. When true, you cannot display previous hashes.
        ///     Default: true.
        /// </summary>
        public bool EnableLocalDatabase { get; private set; } = true;

        /// <summary>
        ///     Switch to enable/disable Registry monitoring.
        ///     Default: false.
        /// </summary>
        public bool EnableRegistryMonitoring { get; private set; }

        /// <summary>
        ///     File extensions to exclude from monitoring.
        ///     Default: Empty list.
        /// </summary>
        public string[] ExcludedExtensions { get; private set; }

        /// <summary>
        ///     Registry keys to exclude from monitoring.
        ///     Default: Empty list.
        /// </summary>
        public string[] ExcludedKeys { get; private set; }

        /// <summary>
        ///     Filesystem directories to exclude from monitoring. Wildcards for folder names are accepted.
        ///     Default: Empty list.
        /// </summary>
        public string[] ExcludedPaths { get; private set; }

        /// <summary>
        ///     Ignore caculating hashes of large files for memory consumption.
        ///     Default: 1024 (1GB)
        /// </summary>
        public int HashLimitMB { get; private set; }

        /// <summary>
        ///     Interval in seconds to send an informational heartbeat log entry to allow monitoring
        ///     of the service itself. It can be disabled by setting it 0.
        ///     Default: 60
        /// </summary>
        public int HeartbeatInterval { get; private set; }

        /// <summary>Maximum raw filesystem notifications held in memory.</summary>
        public int CaptureQueueCapacity { get; private set; } = 8192;

        /// <summary>FileSystemWatcher native buffer size in KiB (8-64).</summary>
        public int WatcherBufferSizeKB { get; private set; } = 64;

        /// <summary>Seconds between wildcard scope re-resolution checks.</summary>
        public int ScopeReresolutionInterval { get; private set; } = 300;

        /// <summary>SHA-256 identity of the canonical effective scope.</summary>
        public string ScopeHash { get; private set; } = string.Empty;

        /// <summary>
        ///     A flag that returns true if file discovery task is completed.
        /// </summary>
        public bool IsFileDiscoveryCompleted
        {
            get
            {
                return Registry.ReadDwordValue("FileDiscoveryCompleted") == 1;
            }
            set
            {
                if (value)
                {
                    Registry.WriteDwordValue("FileDiscoveryCompleted", 1);
                }
                else
                {
                    Registry.WriteDwordValue("FileDiscoveryCompleted", 0);
                }
            }
        }

        /// <summary>
        ///     Registry keys to monitor.
        ///     Default: Empty list.
        /// </summary>
        public string[] MonitoredKeys { get; private set; }

        /// <summary>
        ///     Filesystem directories to monitor. Wildcards for folder names are accepted.
        ///     Default: Empty list.
        /// </summary>
        public string[] MonitoredPaths { get; private set; }

        /// <summary>
        ///     A flag that returns true if application loads the Settings successfully.
        /// </summary>
        public bool Success { get; }

        private const int DEFAULT_HASHLIMIT_MB = 1024;

        private const int DEFAULT_HEARTBEAT_INTERVAL = 60;

        private Regex? excludedExtensionsPattern;

        private Regex? excludedKeysPattern;

        private Regex? excludedPathsPattern;

        private Regex monitoredKeysPattern;

        private Regex monitoredPathsPattern;

        private RegistryScopeMatcher registryScopeMatcher;

        private readonly object reloadLock = new();

        public string? FailureReason { get; private set; }

        /// <summary>
        ///     Creates the application settings managed by the host's dependency injection container.
        /// </summary>
        /// <exception cref="IOException">
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// </exception>
        public Settings()
        {
            _ = Directory.CreateDirectory(Directory.GetParent(DatabasePath)!.ToString());
            try
            {
                ReadOrCreateRegistrySettings();
                Success = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                FailureReason = ex.Message;
                Success = false;
            }
        }

        /// <summary>
        ///     Filters out the initial list
        /// </summary>
        /// <param name="paths">
        ///     Initial list of file paths
        /// </param>
        /// <returns>
        ///     Filtered out fil paths
        /// </returns>
        /// <exception cref="RegexMatchTimeoutException">
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        /// <exception cref="AggregateException">
        /// </exception>
        /// <exception cref="OverflowException">
        /// </exception>
        public List<string> FilterPaths(IEnumerable<string> paths)
        {
            var matches = FilterMonitoredPaths(paths);

            matches = FilterOutExcludedPaths(matches);

            matches = FilterOutExcludedExtensions(matches);

            return matches.ToList();
        }

        public bool IsMonitoredKey(string keyName)
        {
            return registryScopeMatcher.IsMatch(keyName);
        }

        public bool IsMonitoredPath(string path)
        {
            var monitored = monitoredPathsPattern!.IsMatch(path);
            var excludedPath = false;
            if (excludedPathsPattern != null)
            {
                excludedPath = excludedPathsPattern.IsMatch(path);
            }

            var excludedExtension = false;
            if (excludedExtensionsPattern != null)
            {
                excludedExtension = excludedExtensionsPattern.IsMatch(path);
            }

            return monitored && !excludedPath && !excludedExtension;
        }

        /// <summary>Re-reads policy/preferences and atomically publishes a newly resolved scope.</summary>
        /// <returns>The previous and current hashes and whether effective scope changed.</returns>
        public (string PreviousHash, string CurrentHash, bool Changed) Reload()
        {
            lock (reloadLock)
            {
                var previous = ScopeHash;
                ReadOrCreateRegistrySettings();
                return (previous, ScopeHash, !string.Equals(previous, ScopeHash, StringComparison.Ordinal));
            }
        }

        private ParallelQuery<string> FilterMonitoredPaths(IEnumerable<string> paths) => from path in paths.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered)
                                                                                         where monitoredPathsPattern!.IsMatch(path)
                                                                                         select path;

        private ParallelQuery<string> FilterOutExcludedExtensions(ParallelQuery<string> matches)
        {
            if (excludedExtensionsPattern == null)
            {
                return matches;
            }
            return from path in matches.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered)
                   where !excludedExtensionsPattern.IsMatch(path)
                   select path;
        }

        private ParallelQuery<string> FilterOutExcludedPaths(ParallelQuery<string> matches)
        {
            if (excludedPathsPattern == null)
            {
                return matches;
            }
            return from path in matches.AsParallel().WithMergeOptions(ParallelMergeOptions.NotBuffered)
                   where !excludedPathsPattern.IsMatch(path)
                   select path;
        }

        /// <summary>
        ///     Generate the excluded extensions related RegEx pattern
        /// </summary>
        /// <returns>
        ///     RegEx pattern
        /// </returns>
        /// <exception cref="OverflowException">
        /// </exception>
        private Regex? GenerateExcludedExtensionsPattern()
        {
            if (ExcludedExtensions.Length > 0)
            {
                var sb = new StringBuilder(20);
                sb.Append("^.*(?:");
                sb.AppendJoin("|", ExcludedExtensions.Select(Regex.Escape));
                sb.Append(")$");
                return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            return null;
        }

        /// <summary>
        ///     Generate the excluded keys related RegEx pattern
        /// </summary>
        /// <returns>
        ///     RegEx pattern
        /// </returns>
        /// <exception cref="OverflowException">
        /// </exception>
        private Regex? GenerateExcludedKeysPattern()
        {
            if (ExcludedKeys.Length > 0)
            {
                var sb = new StringBuilder(100);
                sb.Append("^(?:");
                sb.AppendJoin("|", ExcludedKeys.Select(Regex.Escape));
                sb.Append(").*$");
                return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            return null;
        }

        /// <summary>
        ///     Generate the excluded paths related RegEx pattern
        /// </summary>
        /// <returns>
        ///     RegEx pattern
        /// </returns>
        /// <exception cref="OverflowException">
        /// </exception>
        private Regex? GenerateExcludedPathsPattern()
        {
            if (ExcludedPaths.Length > 0)
            {
                var sb = new StringBuilder(100);
                sb.Append("^(?:");
                sb.AppendJoin("|", ExcludedPaths.Select(Regex.Escape));
                sb.Append(").*$");
                return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            return null;
        }

        /// <summary>
        ///     Generate the monitored keys related RegEx pattern
        /// </summary>
        /// <returns>
        ///     RegEx pattern
        /// </returns>
        /// <exception cref="OverflowException">
        /// </exception>
        private Regex GenerateMonitoredKeysPattern()
        {
            var sb = new StringBuilder(100);
            sb.Append("^(?:\"?(");
            sb.AppendJoin("|", MonitoredKeys.Select(Regex.Escape));
            sb.Append(")).*$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        /// <summary>
        ///     Generate the monitored paths related RegEx pattern
        /// </summary>
        /// <returns>
        ///     RegEx pattern
        /// </returns>
        /// <exception cref="OverflowException">
        /// </exception>
        private Regex GenerateMonitoredPathsPattern()
        {
            var sb = new StringBuilder(100);
            sb.Append("(?:^(");
            sb.AppendJoin("|", MonitoredPaths.Select(Regex.Escape));
            sb.Append(@")\\?.*$)");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        /// <summary>
        ///     Reads the registry settings and loads into memory. If the registry keys do not
        ///     exist, creates the keys and values, writes the default value data.
        /// </summary>
        /// <remarks>
        ///     Ideally, when it is managed by Group Policy, we need to use a separate key to
        ///     prevent accidental overwrites.
        /// </remarks>
        /// <exception cref="OverflowException">
        /// </exception>
        private void ReadOrCreateRegistrySettings()
        {
            if (string.IsNullOrEmpty(Registry.ReadStringValue("DatabasePath")))
            {
                Registry.WriteStringValue("DatabasePath", DatabasePath);
            }

            var monitoredPaths = Registry.ReadMultiStringValue("MonitoredPaths");
            if (!Registry.EffectiveValueExists("MonitoredPaths"))
            {
                monitoredPaths = [
                    "%SystemRoot%\\System32",
                    "%SystemRoot%\\SysWOW64",
                    "%ProgramFiles%",
                    "%ProgramFiles(x86)%",
                    "%PROGRAMDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup",
                    "%SYSTEMDRIVE%\\Users\\*\\Downloads",
                    "%SYSTEMDRIVE%\\Users\\*\\Documents\\PowerShell",
                    "%SYSTEMDRIVE%\\Users\\*\\Documents\\WindowsPowerShell"];

                Registry.WriteMultiStringValue("MonitoredPaths", monitoredPaths);
            }

            MonitoredPaths = monitoredPaths
                .Select(Environment.ExpandEnvironmentVariables) // Expand variables like %WINDIR%
                .Select(FileSystem.ResolveWildcardPath) // Resolve wildcard in paths like "%SYSTEMDRIVE%\\Users\\*\\Downloads"
                .SelectMany(x => x) // Flatten the list of paths, as resolving wildcard ends up with a list of paths
                .Order().ToArray();
            monitoredPathsPattern = GenerateMonitoredPathsPattern();

            var excludedPaths = Registry.ReadMultiStringValue("ExcludedPaths");
            if (!Registry.EffectiveValueExists("ExcludedPaths"))
            {
                excludedPaths = [@"%SystemRoot%\System32\winevt",
                    @"%SystemRoot%\System32\sru",
                    @"%SystemRoot%\System32\config",
                    @"%SystemRoot%\System32\catroot2",
                    @"%SystemRoot%\System32\LogFiles",
                    @"%SystemRoot%\System32\wbem",
                    @"%SystemRoot%\System32\WDI\LogFiles",
                    @"%SystemRoot%\System32\Microsoft\Protect\Recovery",
                    @"%SystemRoot%\SysWOW64\winevt",
                    @"%SystemRoot%\SysWOW64\sru",
                    @"%SystemRoot%\SysWOW64\config",
                    @"%SystemRoot%\SysWOW64\catroot2",
                    @"%SystemRoot%\SysWOW64\LogFiles",
                    @"%SystemRoot%\SysWOW64\wbem",
                    @"%SystemRoot%\SysWOW64\WDI\LogFiles",
                    @"%SystemRoot%\SysWOW64\Microsoft\Protect\Recovery",
                    @"%ProgramFiles%\Windows Defender Advanced Threat Protection\Classification\Configuration",
                    @"%ProgramFiles%\Microsoft OneDrive\StandaloneUpdater\logs"];
                Registry.WriteMultiStringValue("ExcludedPaths", excludedPaths);
            }
            ExcludedPaths = excludedPaths
                .Select(Environment.ExpandEnvironmentVariables) // Expand variables like %WINDIR%
                .Select(FileSystem.ResolveWildcardPath) // Resolve wildcard in paths like "%SYSTEMDRIVE%\\Users\\*\\Downloads"
                .SelectMany(x => x) // Flatten the list of paths, as resolving wildcard ends up with a list of paths
                .Order().ToArray();
            excludedPathsPattern = GenerateExcludedPathsPattern();

            var excludedExtensions = Registry.ReadMultiStringValue("ExcludedExtensions");
            if (!Registry.EffectiveValueExists("ExcludedExtensions"))
            {
                excludedExtensions = [".log", ".evtx", ".etl", ".wal", ".db-wal", ".db"];
                Registry.WriteMultiStringValue("ExcludedExtensions", excludedExtensions);
            }
            ExcludedExtensions = excludedExtensions.Order().ToArray();
            excludedExtensionsPattern = GenerateExcludedExtensionsPattern();

            var registryMonitoring = Registry.ReadDwordValue("EnableRegistryMonitoring");
            if (registryMonitoring == -1)
            {
                Registry.WriteDwordValue("EnableRegistryMonitoring", 1);
                registryMonitoring = 1;
            }
            EnableRegistryMonitoring = registryMonitoring == 1;

            var monitoredKeys = Registry.ReadMultiStringValue("MonitoredKeys");
            if (!Registry.EffectiveValueExists("MonitoredKeys"))
            {
                monitoredKeys = [
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WinFIMLog",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Microsoft Defender",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders",
                    @"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunServicesOnce",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunServices",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Windows",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunServicesOnce",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunServices",
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL",
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002",
                    @"HKEY_CURRENT_USER\Software\Classes\Mscfile\Shell\Open\Command",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\Control.exe",
                    @"HKEY_CURRENT_USER\Software\Classes\Exefile\Shell\Runas\Command\IsolatedCommand",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows Nt\CurrentVersion\Imagefileexecutionoptions",
                    @"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\USBTor",
                    @"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Enum\USB",
                    @"HKEY_CURRENT_USER\Environment",
                    @"HKEY_CURRENT_USER\Control Panel\Desktop\Scrnsave.exe",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Command Processor\Autorun",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\Desktop\Components",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\Explorer Bars",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\Extensions",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\UrlSearchHooks\Server\Install\Software\Microsoft\Windows\CurrentVersion\Run",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Windows\Run",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Winlogon",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Run",
                    @"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Control Panel\Desktop\Scrnsave.exe",
                    @"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\System\Scripts\Logoff",
                    @"HKEY_CURRENT_USER\Software\Wow6432Node\Microsoft\Internet Explorer\Explorer Bars",
                    @"HKEY_CURRENT_USER\Software\Wow6432Node\Microsoft\Internet Explorer\Extensions",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Winlogon",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Notify",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\System",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Taskman",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\GroupPolicy\Scripts\Shutdown",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\GroupPolicy\Scripts\Startup",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\System\Shell",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run",
                    @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunOnce",
                    @"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System\Scripts\Logoff",
                    @"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System\Scripts\Logon",
                    @"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System\Scripts\Shutdown",
                    @"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\System\Scripts\Startup",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Command\Processor\Autorun",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Internet Explorer\Explorer Bars",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Internet Explorer\Extensions",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Internet Explorer\Toolbar",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
                    @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
                    @"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\LSA",
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Keyboard Layout",
                    @"HKEY_CURRENT_USER\Keyboard Layout\Preload"];

                Registry.WriteMultiStringValue("MonitoredKeys", monitoredKeys);
            }
            var effectiveMonitoredKeys = monitoredKeys.ToList();
            ScopeIdentity.EnsureConfigurationKeysMonitored(effectiveMonitoredKeys);
            MonitoredKeys = effectiveMonitoredKeys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            monitoredKeysPattern = GenerateMonitoredKeysPattern();

            var excludedKeys = Registry.ReadMultiStringValue("ExcludedKeys");
            if (!Registry.EffectiveValueExists("ExcludedKeys"))
            {
                excludedKeys = [string.Empty];
                Registry.WriteMultiStringValue("ExcludedKeys", excludedKeys);
            }
            ExcludedKeys = excludedKeys.Order().ToArray();
            excludedKeysPattern = ExcludedKeys.Length == 1 && ExcludedKeys[0]?.Length == 0 ? null : GenerateExcludedKeysPattern();

            ConfigurationValidator.Validate(monitoredPaths, excludedPaths, MonitoredKeys, ExcludedKeys);
            registryScopeMatcher = new RegistryScopeMatcher(MonitoredKeys, ExcludedKeys);

            ScopeHash = ScopeIdentity.Compute(MonitoredPaths, ExcludedPaths, ExcludedExtensions, MonitoredKeys, ExcludedKeys);

            var heartbeat = Registry.ReadDwordValue("HeartbeatInterval");
            if (heartbeat == -1)
            {
                Registry.WriteDwordValue("HeartbeatInterval", DEFAULT_HEARTBEAT_INTERVAL);
                heartbeat = DEFAULT_HEARTBEAT_INTERVAL;
            }

            HeartbeatInterval = heartbeat;

            var captureQueueCapacity = Registry.ReadDwordValue("CaptureQueueCapacity");
            if (captureQueueCapacity == -1)
            {
                captureQueueCapacity = 8192;
                Registry.WriteDwordValue("CaptureQueueCapacity", captureQueueCapacity);
            }
            if (captureQueueCapacity < 1) throw new InvalidOperationException("CaptureQueueCapacity must be greater than zero.");
            CaptureQueueCapacity = captureQueueCapacity;

            var watcherBufferSizeKb = Registry.ReadDwordValue("WatcherBufferSizeKB");
            if (watcherBufferSizeKb == -1)
            {
                watcherBufferSizeKb = 64;
                Registry.WriteDwordValue("WatcherBufferSizeKB", watcherBufferSizeKb);
            }
            if (watcherBufferSizeKb is < 8 or > 64) throw new InvalidOperationException("WatcherBufferSizeKB must be between 8 and 64.");
            WatcherBufferSizeKB = watcherBufferSizeKb;

            var scopeInterval = Registry.ReadDwordValue("ScopeReresolutionInterval");
            if (scopeInterval == -1)
            {
                scopeInterval = 300;
                Registry.WriteDwordValue("ScopeReresolutionInterval", scopeInterval);
            }
            if (scopeInterval < 10) throw new InvalidOperationException("ScopeReresolutionInterval must be at least 10 seconds.");
            ScopeReresolutionInterval = scopeInterval;

            var enableLocalDatabase = Registry.ReadDwordValue("EnableLocalDatabase");
            if (enableLocalDatabase == -1)
            {
                Registry.WriteDwordValue("EnableLocalDatabase", 1);
                enableLocalDatabase = 1;
            }
            EnableLocalDatabase = enableLocalDatabase == 1;

            var fileDiscoveryCompleted = Registry.ReadDwordValue("FileDiscoveryCompleted");
            if (fileDiscoveryCompleted == -1)
            {
                IsFileDiscoveryCompleted = false;
            }

            var hashLimitMb = Registry.ReadDwordValue("HashLimitMB");
            if (hashLimitMb == -1)
            {
                Registry.WriteDwordValue("HashLimitMB", DEFAULT_HASHLIMIT_MB);
                hashLimitMb = DEFAULT_HASHLIMIT_MB;
            }

            HashLimitMB = hashLimitMb;
        }

    }
}
