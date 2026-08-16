using System;
using System.Collections.Generic;
using System.IO;
using FastCache;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.IO;
using WinFIMLog.Utils;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Jobs
{
    internal partial class FileSystemMonitorJob : IMonitor
    {
        private readonly ILiteDbContext _ctx;

        /// <summary>
        ///     Windows file system creates multiple events for creation and change events. These
        ///     are by design but creates pollution. It is impossible to remove all of them but it
        ///     can be minimized. For this, a buffer is used to check duplicate records.
        /// </summary>
        /// <see href="https://devblogs.microsoft.com/oldnewthing/20140507-00/?p=1053" />

        private readonly ILogger _logger;

        private readonly IBuffer<FileSystemChange> _messageStore;

        private readonly List<FileSystemWatcher> _watchers;

        private bool _disposedValue;

        public FileSystemMonitorJob(ILogger logger, IBuffer<FileSystemChange> fsStore, ILiteDbContext ctx)
        {
            _logger = logger;
            _watchers = [];
            _messageStore = fsStore;
            _ctx = ctx;
        }

        // This should run async
        public void Start() => InvokeWatchers();

        public void Stop()
        {
            if (_watchers.Count == 0) return;

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

        private void InvokeWatchers()
        {
            foreach (var path in Settings.Instance.MonitoredPaths)
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
                    EnableRaisingEvents = true,
                    Filter = string.Empty
                };

                watcher.Changed += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;

                watcher.Error += OnError;

                _watchers.Add(watcher);
                _logger.LogInformation("Initiated file system watcher for directory {directory}", path);
            }
        }

        private static string GetFingerprint(FileSystemChange change) =>
            $"{change.ChangeCategory}\0{change.ObjectType}\0{change.CurrentHash}\0{change.ACLs}";

        private static bool IsDuplicate(FileSystemChange change, string fingerprint) =>
            Cached<string>.TryGet(change.FullPath, out var cached) && cached == fingerprint;

        private static bool ShouldAdd(FileSystemChange change, FileSystemChange? previous)
        {
            return change.ChangeCategory is ChangeCategory.Created or ChangeCategory.Deleted ||
                   previous == null ||
                   change.ObjectType != previous.ObjectType ||
                   !string.Equals(change.CurrentHash, previous.CurrentHash, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(change.ACLs, previous.ACLs, StringComparison.Ordinal);
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Changed);

        private void OnCreated(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Created);

        private void OnDeleted(object sender, FileSystemEventArgs e) => ProcessEvent(e.FullPath, ChangeCategory.Deleted);

        private void OnError(object sender, ErrorEventArgs e) => e.GetException().Log(_logger);

        private void ProcessEvent(string path, ChangeCategory category)
        {
            if (!Settings.Instance.IsMonitoredPath(path))
            {
                return;
            }

            var change = FileSystemChange.FromPath(path, category);

            if (change != null)
            {
                FileSystemChange? previous = null;
                if (Settings.Instance.EnableLocalDatabase)
                {
                    previous = FileSystemChange.RetrievePreviousChange(path, _ctx);
                    change.PreviousHash = previous?.CurrentHash ?? string.Empty;
                }

                var fingerprint = GetFingerprint(change);
                if (ShouldAdd(change, previous) && !IsDuplicate(change, fingerprint))
                {
                    _messageStore.Add(change);
                    Cached<string>.Save(path, fingerprint, TimeSpan.FromSeconds(5));
                }
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
