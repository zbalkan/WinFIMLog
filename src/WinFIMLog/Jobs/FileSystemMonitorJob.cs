using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Jobs
{
    internal partial class FileSystemMonitorJob : IMonitor
    {
        private readonly FileSystemBaselineAvailability _baselineAvailability;
        private readonly FileSystemCaptureQueue _capture;
        private readonly IHealthReporter _health;
        private readonly ILogger _logger;
        private readonly Settings _settings;
        private readonly ISnapshotCoordinator _snapshots;
        private readonly Lock _watcherLock = new();
        private readonly List<FileSystemWatcher> _watchers;
        private bool _disposedValue;
        private bool _stopping;

        public FileSystemMonitorJob(ILogger logger, FileSystemCaptureQueue capture, IHealthReporter health,
            Settings settings, ISnapshotCoordinator snapshots, FileSystemBaselineAvailability baselineAvailability)
        {
            _logger = logger;
            _watchers = [];
            _capture = capture;
            _health = health;
            _settings = settings;
            _snapshots = snapshots;
            _baselineAvailability = baselineAvailability;
        }

        /// <summary>Applies watcher additions and removals for the newly resolved scope.</summary>
        public void Reconfigure()
        {
            var configuration = _settings.Capture();
            _baselineAvailability.Refresh(configuration);
            var desired = new HashSet<string>(configuration.MonitoredPaths, StringComparer.OrdinalIgnoreCase);
            lock (_watcherLock)
            {
                if (_stopping)
                {
                    return;
                }

                foreach (var watcher in _watchers.ToArray())
                {
                    if (desired.Contains(watcher.Path) &&
                        watcher.InternalBufferSize == configuration.WatcherBufferSizeKB * 1024)
                    {
                        continue;
                    }

                    DisposeWatcher(watcher);
                    _watchers.Remove(watcher);
                    _logger.LogInformation("Removed file system watcher for directory {Directory}", watcher.Path);
                }
                foreach (var path in desired.Where(path => !_watchers.Exists(watcher => string.Equals(watcher.Path, path, StringComparison.OrdinalIgnoreCase))))
                {
                    _watchers.Add(CreateWatcher(path, configuration));
                    _logger.LogInformation("Added file system watcher for directory {Directory}", path);
                }
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            InvokeWatchers();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal source shutdown.
            }
            finally
            {
                Stop();
            }
        }

        private static string? SenderPath(EffectiveSettings configuration, string path) =>
            Array.Find(configuration.MonitoredPaths, p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        private FileSystemWatcher CreateWatcher(string path, EffectiveSettings configuration)
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.Attributes
                                | NotifyFilters.CreationTime
                                | NotifyFilters.DirectoryName
                                | NotifyFilters.FileName

                                // | NotifyFilters.LastAccess // This creates so much bloat.
                                | NotifyFilters.LastWrite
                                | NotifyFilters.Security
                                | NotifyFilters.Size,
                IncludeSubdirectories = true,
                InternalBufferSize = configuration.WatcherBufferSizeKB * 1024,
                Filter = string.Empty,
                EnableRaisingEvents = false
            };

            watcher.Changed += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;

            watcher.Error += OnError;

            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void DisposeWatcher(FileSystemWatcher watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Created -= OnCreated;
            watcher.Deleted -= OnDeleted;
            watcher.Error -= OnError;
            watcher.Dispose();
        }

        private void InvokeWatchers()
        {
            var configuration = _settings.Capture();
            lock (_watcherLock)
            {
                if (_stopping)
                {
                    return;
                }

                foreach (var path in configuration.MonitoredPaths)
                {
                    var watcher = CreateWatcher(path, configuration);
                    _watchers.Add(watcher);
                    _logger.LogInformation("Initiated file system watcher for directory {directory}", path);
                }
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Changed);

        private void OnCreated(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Created);

        private void OnDeleted(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Deleted);

        private void OnError(object sender, ErrorEventArgs e)
        {
            if (sender is not FileSystemWatcher failed)
            {
                return;
            }

            string? scope = null;
            Exception? restartFailure = null;
            try
            {
                lock (_watcherLock)
                {
                    if (_stopping || !_watchers.Contains(failed))
                    {
                        return;
                    }

                    scope = failed.Path;
                    _watchers.Remove(failed);
                    DisposeWatcher(failed);
                    var configuration = _settings.Capture();
                    if (!configuration.MonitoredPaths.Contains(scope, StringComparer.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _watchers.Add(CreateWatcher(scope, configuration));
                }
            }
            catch (Exception exception)
            {
                restartFailure = exception;
            }

            if (scope is null)
            {
                return;
            }

            _health.CoverageGap("FileSystemWatcher", scope, e.GetException().GetType().Name);
            if (restartFailure is not null)
            { _health.CoverageGap("FileSystemWatcher", scope, $"RestartFailed:{restartFailure.GetType().Name}"); return; }
            _snapshots.RequestFileSystemSnapshot("Watcher source failure", scope);
            _health.SourceRecovered("FileSystemWatcher", scope, "WatcherRecreated;ReconciliationStarted");
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            var configuration = _settings.Capture();
            if (!_baselineAvailability.IsEstablished(configuration) ||
                (!configuration.IsMonitoredPath(e.FullPath) && !configuration.IsMonitoredPath(e.OldFullPath)))
            {
                return;
            }

            _capture.TryAdmit(new RawFileSystemNotification(SenderPath(configuration, e.FullPath) ?? SenderPath(configuration, e.OldFullPath) ?? string.Empty,
                e.FullPath, ChangeCategory.Changed, DateTimeOffset.UtcNow, e.OldFullPath, e.FullPath));
        }

        private void ProcessEvent(string path, ChangeCategory category)
        {
            var configuration = _settings.Capture();
            if (!configuration.IsMonitoredPath(path) || !_baselineAvailability.IsEstablished(configuration))
            {
                return;
            }

            _capture.TryAdmit(new RawFileSystemNotification(
                SenderPath(configuration, path) ?? string.Empty, path, category, DateTimeOffset.UtcNow));
        }

        private void Stop()
        {
            lock (_watcherLock)
            {
                if (_stopping)
                {
                    return;
                }

                _stopping = true;
                foreach (var watcher in _watchers)
                {
                    DisposeWatcher(watcher);
                }

                _watchers.Clear();
            }
        }

        #region Dispose

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                }

                _disposedValue = true;
            }
        }

        #endregion Dispose
    }
}
