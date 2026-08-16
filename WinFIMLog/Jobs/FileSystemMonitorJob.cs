using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Jobs
{
    internal partial class FileSystemMonitorJob : IMonitor
    {
        private readonly ILogger _logger;
        private readonly FileSystemCaptureQueue _capture;
        private readonly IHealthReporter _health;
        private readonly Action<string> _reconcile;

        private readonly List<FileSystemWatcher> _watchers;

        private readonly Settings _settings;

        private bool _disposedValue;

        public FileSystemMonitorJob(ILogger logger, FileSystemCaptureQueue capture, IHealthReporter health, Settings settings, Action<string> reconcile)
        {
            _logger = logger;
            _watchers = [];
            _capture = capture;
            _health = health;
            _settings = settings;
            _reconcile = reconcile;
        }

        // This should run async
        public void Start()
        {
            InvokeWatchers();
        }

        public void Stop()
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnChanged;
                watcher.Renamed -= OnChanged;
                watcher.Created -= OnCreated;
                watcher.Deleted -= OnDeleted;
                watcher.Error -= OnError;
                watcher.Dispose();
            }
            _watchers.Clear();
        }

        /// <summary>Applies watcher additions and removals for the newly resolved scope.</summary>
        public void Reconfigure()
        {
            var desired = new HashSet<string>(_settings.MonitoredPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var watcher in _watchers.ToArray())
            {
                if (desired.Contains(watcher.Path)) continue;
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(watcher);
                _logger.LogInformation("Removed file system watcher for directory {Directory}", watcher.Path);
            }
            foreach (var path in desired.Where(path => !_watchers.Exists(watcher => string.Equals(watcher.Path, path, StringComparison.OrdinalIgnoreCase))))
            {
                _watchers.Add(CreateWatcher(path));
                _logger.LogInformation("Added file system watcher for directory {Directory}", path);
            }
        }

        private void InvokeWatchers()
        {
            foreach (var path in _settings.MonitoredPaths)
            {
                var watcher = CreateWatcher(path);

                _watchers.Add(watcher);
                _logger.LogInformation("Initiated file system watcher for directory {directory}", path);
            }
        }

        private FileSystemWatcher CreateWatcher(string path)
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
                    InternalBufferSize = _settings.WatcherBufferSizeKB * 1024,
                    Filter = string.Empty,
                    EnableRaisingEvents = false
                };

                watcher.Changed += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;

                watcher.Error += OnError;

            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Changed);

        private void OnCreated(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Created);

        private void OnDeleted(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Deleted);

        private void OnError(object sender, ErrorEventArgs e)
        {
            if (sender is not FileSystemWatcher failed) return;
            var scope = failed.Path;
            _health.CoverageGap("FileSystemWatcher", scope, e.GetException().GetType().Name);
            failed.EnableRaisingEvents = false;
            failed.Dispose();
            _watchers.Remove(failed);
            try
            {
                _watchers.Add(CreateWatcher(scope));
                _reconcile(scope);
                _health.SourceRecovered("FileSystemWatcher", scope, "WatcherRecreated;ReconciliationStarted");
            }
            catch (Exception exception)
            {
                _health.CoverageGap("FileSystemWatcher", scope, $"RestartFailed:{exception.GetType().Name}");
            }
        }

        private void ProcessEvent(string path, ChangeCategory category)
        {
            if (!_settings.IsMonitoredPath(path))
            {
                return;
            }

            _capture.TryAdmit(new RawFileSystemNotification(
                (senderPath(path) ?? string.Empty), path, category, DateTimeOffset.UtcNow));
        }

        private string? senderPath(string path) => Array.Find(_settings.MonitoredPaths, p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

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
