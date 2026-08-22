using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;
using WinFIMLog.Utils;
using NtKeywords = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords;

namespace WinFIMLog.Jobs
{
    /// <summary>
    /// A class capturing Registry events.
    /// </summary>
    /// <see href="https://github.com/lowleveldesign/lowleveldesign-blog-samples/blob/master/monitoring-registry-activity-with-etw/Program.fs" />
    internal class RegistryMonitorJob : IMonitor
    {
        private const string ETWSessionName = "RegistryWatcher";

        private const int SessionBufferSizeMegabytes = 64;

        private const NtKeywords TraceFlags = NtKeywords.Registry;

        private readonly RegistryKcbCache _keyCache = new();
        private readonly ILogger _logger;

        private readonly IBuffer<RegistryChange> _messageStore;

        private readonly int _pid;

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
        /// Start monitoring selected Registry keys
        /// </summary>
        public Task RunAsync(CancellationToken cancellationToken) => RunAsync(sourceStarted: null, cancellationToken);

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
                await using var lossPoll = new Timer(_ => ReportEventLoss(session), state: null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
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
        /// Stop monitoring selected Registry keys
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        private void CleanupExistingSession()
        {
            try
            {
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(ETWSessionName))
                {
                    using var session = new TraceEventSession(ETWSessionName, TraceEventSessionOptions.Attach)
                    {
                        StopOnDispose = true,
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
                BufferSizeMB = SessionBufferSizeMegabytes,
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

        private string GetFullKeyName(ulong keyHandle, string eventKeyName, string eventValueName) =>
            CombineFullKeyName(keyHandle != 0 && _keyCache.TryGet(keyHandle, out var keyName) ? keyName : null,
                eventKeyName, eventValueName);

        internal static string CombineFullKeyName(string? cachedKeyName, string? eventKeyName,
            string? eventValueName)
        {
            var segmentCount = CountSegments(cachedKeyName, eventKeyName, eventValueName);
            if (segmentCount is 0)
            {
                return string.Empty;
            }

            var length = GetSegmentLength(cachedKeyName) + GetSegmentLength(eventKeyName) +
                         GetSegmentLength(eventValueName) + segmentCount - 1;
            var fullName = string.Create(length, (cachedKeyName, eventKeyName, eventValueName),
                static (destination, segments) =>
                {
                    var written = 0;
                    AppendSegment(destination, ref written, segments.cachedKeyName);
                    AppendSegment(destination, ref written, segments.eventKeyName);
                    AppendSegment(destination, ref written, segments.eventValueName);
                });

            return NormalizeRegistryHivePrefixes(fullName);
        }

        private static int CountSegments(string? cachedKeyName, string? eventKeyName, string? eventValueName) =>
            (string.IsNullOrWhiteSpace(cachedKeyName) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(eventKeyName) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(eventValueName) ? 0 : 1);

        private static int GetSegmentLength(string? value) =>
            string.IsNullOrWhiteSpace(value) ? 0 : value.Length;

        private static void AppendSegment(Span<char> destination, ref int written, string? segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            if (written > 0)
            {
                destination[written++] = '\\';
            }

            segment.AsSpan().CopyTo(destination[written..]);
            written += segment.Length;
        }

        private static string NormalizeRegistryHivePrefixes(string fullName)
        {
            const string machinePrefix = "\\REGISTRY\\MACHINE";
            const string userPrefix = "\\REGISTRY\\USER";
            const string machineReplacement = "HKEY_LOCAL_MACHINE";
            const string userReplacement = "HKEY_USERS";

            var source = fullName.AsSpan();
            var normalizedLength = NormalizedLength(source, machinePrefix, machineReplacement,
                userPrefix, userReplacement);
            if (normalizedLength == source.Length)
            {
                return fullName;
            }

            return string.Create(normalizedLength, fullName, static (destination, value) =>
            {
                const string machinePrefix = "\\REGISTRY\\MACHINE";
                const string userPrefix = "\\REGISTRY\\USER";
                const string machineReplacement = "HKEY_LOCAL_MACHINE";
                const string userReplacement = "HKEY_USERS";

                var source = value.AsSpan();
                var written = 0;
                for (var index = 0; index < source.Length;)
                {
                    if (source[index] == '\\' &&
                        source[index..].StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        machineReplacement.AsSpan().CopyTo(destination[written..]);
                        written += machineReplacement.Length;
                        index += machinePrefix.Length;
                    }
                    else if (source[index] == '\\' &&
                             source[index..].StartsWith(userPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        userReplacement.AsSpan().CopyTo(destination[written..]);
                        written += userReplacement.Length;
                        index += userPrefix.Length;
                    }
                    else
                    {
                        destination[written++] = source[index++];
                    }
                }
            });
        }

        private static int NormalizedLength(ReadOnlySpan<char> source, string machinePrefix,
            string machineReplacement, string userPrefix, string userReplacement)
        {
            var length = 0;
            for (var index = 0; index < source.Length;)
            {
                if (source[index] == '\\' &&
                    source[index..].StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    length += machineReplacement.Length;
                    index += machinePrefix.Length;
                }
                else if (source[index] == '\\' &&
                         source[index..].StartsWith(userPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    length += userReplacement.Length;
                    index += userPrefix.Length;
                }
                else
                {
                    length++;
                    index++;
                }
            }

            return length;
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
                        ScopeHash = configuration.ScopeHash,
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
