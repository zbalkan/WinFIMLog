using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;
using WinFIMLog.Utils;
using NtKeywords = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords;

namespace WinFIMLog.Jobs
{
    /// <summary>
    ///     A class capturing Registry events.
    /// </summary>
    /// <see href="https://github.com/lowleveldesign/lowleveldesign-blog-samples/blob/master/monitoring-registry-activity-with-etw/Program.fs" />
    internal partial class RegistryMonitorJob : IMonitor
    {
        private const string ETWSessionName = "RegistryWatcher";

        private const int SessionBufferSizeMegabytes = 64;

        private const NtKeywords TraceFlags = NtKeywords.Registry;

        private readonly RegistryKcbCache _keyCache = new();
        private readonly ILogger _logger;

        private readonly IBuffer<RegistryChange> _messageStore;

        private readonly int _pid;

        private readonly ObjectPool<StringBuilder> _sbPool = new DefaultObjectPoolProvider().CreateStringBuilderPool();
        private readonly Lock _sessionLock = new();
        private readonly Settings _settings;
        private readonly ISnapshotCoordinator _snapshots;
        private bool _disposedValue;
        private long _reportedEventsLost;
        private TraceEventSession? _session;

        public RegistryMonitorJob(ILogger logger, IBuffer<RegistryChange> regStore, Settings settings, ISnapshotCoordinator snapshots)
        {
            _logger = logger;
            _pid = Environment.ProcessId;
            _messageStore = regStore;
            _settings = settings;
            _snapshots = snapshots;
        }

        /// <summary>
        ///     Start monitoring selected Registry keys
        /// </summary>
        public Task RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

        public async Task RunAsync(Action? sourceStarted, CancellationToken cancellationToken)
        {
            CleanupExistingSession();
            using var session = CreateSession();
            lock (_sessionLock)
            {
                _session = session;
            }

            try
            {
                await using var cancellationRegistration = cancellationToken.Register(() => session.Stop());
                _logger.LogInformation("Started ETW session '{SessionName}' for Registry changes.", ETWSessionName);
                sourceStarted?.Invoke();
                await using var lossPoll = new Timer(_ => ReportEventLoss(session), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                await Task.Run(session.Source.Process, CancellationToken.None);
                ReportEventLoss(session);
            }
            finally
            {
                lock (_sessionLock)
                {
                    if (ReferenceEquals(_session, session))
                    {
                        _session = null;
                    }
                }
            }
        }

        /// <summary>
        ///     Stop monitoring selected Registry keys
        /// </summary>
        /// <exception cref="AggregateException">
        /// </exception>
        private void CleanupExistingSession()
        {
            try
            {
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(ETWSessionName))
                {
                    using var session = new TraceEventSession(ETWSessionName, TraceEventSessionOptions.Attach)
                    {
                        StopOnDispose = true
                    };
                    _logger.LogInformation("Cleaned up lingering ETW session 'RegistryWatcher' from a previous run.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while checking or cleaning up lingering ETW session.");
            }
        }

        private TraceEventSession CreateSession()
        {
            var session = new TraceEventSession(ETWSessionName)
            {
                StopOnDispose = true,
                BufferSizeMB = SessionBufferSizeMegabytes
            };

            try
            {
                session.EnableKernelProvider(TraceFlags);
                var kernel = new KernelTraceEventParser(
                    session.Source,
                    KernelTraceEventParser.ParserTrackingOptions.None);

                kernel.RegistryKCBRundownEnd += UpdateCache;
                kernel.RegistryKCBCreate += UpdateCache;
                kernel.RegistryKCBDelete += DeleteCache;
                kernel.RegistryCreate += ProcessEvent;
                kernel.RegistryDelete += ProcessEvent;
                kernel.RegistrySetValue += ProcessEvent;
                kernel.RegistryDeleteValue += ProcessEvent;
                kernel.RegistrySetInformation += ProcessEvent;
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private void DeleteCache(RegistryTraceData data) =>
            _keyCache.Remove(data.KeyHandle);

        private string GetFullKeyName(ulong keyHandle, string eventKeyName, string eventValueName)
        {
            if (string.IsNullOrWhiteSpace(eventKeyName) && string.IsNullOrWhiteSpace(eventValueName))
            {
                return string.Empty;
            }

            var fullNameBuilder = _sbPool.Get();

            if (keyHandle != 0 && _keyCache.TryGet(keyHandle, out var keyName))
            {
                fullNameBuilder.Append(keyName);
            }

            if (!string.IsNullOrWhiteSpace(eventKeyName))
            {
                if (fullNameBuilder.Length > 0)
                {
                    fullNameBuilder.Append('\\');
                }

                fullNameBuilder.Append(eventKeyName);
            }

            if (!string.IsNullOrWhiteSpace(eventValueName))
            {
                if (fullNameBuilder.Length > 0)
                {
                    fullNameBuilder.Append('\\');
                }

                fullNameBuilder.Append(eventValueName);
            }

            var fullName = fullNameBuilder.ToString();
            _sbPool.Return(fullNameBuilder);

            fullName = RegistryMachineRegex().Replace(fullName, "HKEY_LOCAL_MACHINE");
            fullName = RegistryUserRegex().Replace(fullName, "HKEY_USERS");

            return fullName;
        }

        private bool IsMonitoredEvent(EffectiveSettings configuration, string keyName, int pid)
        {
            if (pid == _pid || pid == -1)
            {
                return false;
            }

            if (string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            return configuration.IsMonitoredKey(keyName);
        }

        private void ProcessEvent(RegistryTraceData ev)
        {
            try
            {
                var keyName = GetFullKeyName(ev.KeyHandle, ev.KeyName, ev.ValueName);
                var configuration = _settings.Capture();

                if (IsMonitoredEvent(configuration, keyName, ev.ProcessID))
                {
                    Debug.WriteLine($"Processing event: {ev.EventName} for {keyName}");
                    var change = new RegistryChange(ev, keyName)
                    {
                        ScopeHash = configuration.ScopeHash
                    };

                    _messageStore.Add(change);
                }
            }
            catch (Exception ex)
            {
                ex.Log(_logger);
            }
        }

        private void ReportEventLoss(TraceEventSession session)
        {
            var total = session.Source.EventsLost;
            var previous = Interlocked.Exchange(ref _reportedEventsLost, total);
            if (total > previous)
            {
                // Loss can include a KCB delete. Discard potentially stale handle bindings;
                // the snapshot requested below is the authoritative recovery path.
                _keyCache.Clear();
                _logger.LogError(7791, "COVERAGE GAP Source=RegistryETW Scope=ConfiguredRegistryKeys Reason=EventsLost LostCount={LostCount}", total - previous);
                _snapshots.RequestRegistrySnapshot("Registry ETW events lost", "ConfiguredRegistryKeys");
            }
        }

        private void UpdateCache(RegistryTraceData data) =>
                                                    _keyCache.Update(data.KeyHandle, data.KeyName);

        #region Regex

        [GeneratedRegex(@"\\REGISTRY\\MACHINE", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex RegistryMachineRegex();

        [GeneratedRegex(@"\\REGISTRY\\USER", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex RegistryUserRegex();

        #endregion Regex

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
                    TraceEventSession? session;
                    lock (_sessionLock)
                    {
                        session = _session;
                    }

                    session?.Stop();
                }

                _disposedValue = true;
            }
        }

        #endregion Dispose
    }
}
