using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FastCache;
using WinFIMLog.FIM;
using WinFIMLog.Utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using NtKeywords = Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser.Keywords;
using WinFIMLog.Snapshots;

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

        private const int SessionBufferQuantumKilobytes = 256;

        private const string TraceEventBufferQuantumFieldName = "m_BufferQuantumKB";

        private const NtKeywords TraceFlags = NtKeywords.Registry;

        private readonly ILogger _logger;

        private readonly IBuffer<RegistryChange> _messageStore;

        private readonly int _pid;

        private readonly Settings _settings;
        private readonly ISnapshotCoordinator _snapshots;

        private readonly ObjectPool<StringBuilder> _sbPool = new DefaultObjectPoolProvider().CreateStringBuilderPool();

        private readonly object _sessionLock = new();

        private TraceEventSession? _session;
        private long _reportedEventsLost;

        private bool _disposedValue;

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
        /// <exception cref="FieldAccessException">
        /// </exception>
        /// <exception cref="TargetException">
        /// </exception>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            CleanupExistingSession();
            using var session = CreateSession();
            lock (_sessionLock)
            {
                _session = session;
            }

            try
            {
                using var cancellationRegistration = cancellationToken.Register(() => session.Stop());
                _logger.LogInformation("Started ETW session '{SessionName}' for Registry changes.", ETWSessionName);
                using var lossPoll = new Timer(_ => ReportEventLoss(session), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
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

        private TraceEventSession CreateSession()
        {
            var session = new TraceEventSession(ETWSessionName)
            {
                StopOnDispose = true,
                BufferSizeMB = SessionBufferSizeMegabytes
            };

            ConfigureBufferSize(session);

            try
            {
                session.EnableKernelProvider(TraceFlags);
                MakeKernelParserStateless(session.Source);

                session.Source.Kernel.RegistryKCBRundownEnd += UpdateCache;
                session.Source.Kernel.RegistryKCBCreate += UpdateCache;
                session.Source.Kernel.RegistryKCBDelete += DeleteCache;
                session.Source.Kernel.RegistryCreate += ProcessEvent;
                session.Source.Kernel.RegistryDelete += ProcessEvent;
                session.Source.Kernel.RegistrySetValue += ProcessEvent;
                session.Source.Kernel.RegistryDeleteValue += ProcessEvent;
                session.Source.Kernel.RegistrySetInformation += ProcessEvent;
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private static void ConfigureBufferSize(TraceEventSession session)
        {
            var field = typeof(TraceEventSession).GetField(
                TraceEventBufferQuantumFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(typeof(TraceEventSession).FullName, TraceEventBufferQuantumFieldName);
            field.SetValue(session, SessionBufferQuantumKilobytes);
        }

        private static void UpdateCache(RegistryTraceData data) =>
            Cached<string>.Save(data.KeyHandle, data.KeyName, TimeSpan.FromHours(1));

        private static void DeleteCache(RegistryTraceData data) =>
            // FastCache has no removal API. Replacing the binding with an immediately expiring
            // value prevents a reused KCB handle being resolved to its deleted path.
            Cached<string>.Save(data.KeyHandle, string.Empty, TimeSpan.FromTicks(1));

        private void ReportEventLoss(TraceEventSession session)
        {
            var total = session.Source.EventsLost;
            var previous = Interlocked.Exchange(ref _reportedEventsLost, total);
            if (total > previous)
            {
                _logger.LogError(7791, "COVERAGE GAP Source=RegistryETW Scope=ConfiguredRegistryKeys Reason=EventsLost LostCount={LostCount}", total - previous);
                _snapshots.RequestRegistrySnapshot("Registry ETW events lost", "ConfiguredRegistryKeys");
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

        private string GetFullKeyName(ulong keyHandle, string eventKeyName, string eventValueName)
        {
            if (string.IsNullOrWhiteSpace(eventKeyName) && string.IsNullOrWhiteSpace(eventValueName))
                return string.Empty;

            var fullNameBuilder = _sbPool.Get();

            if (keyHandle != 0 && Cached<string>.TryGet(keyHandle, out var keyName))
            {
                fullNameBuilder.Append(keyName);
            }

            if (!string.IsNullOrWhiteSpace(eventKeyName))
            {
                if (fullNameBuilder.Length > 0) fullNameBuilder.Append('\\');
                fullNameBuilder.Append(eventKeyName);
            }

            if (!string.IsNullOrWhiteSpace(eventValueName))
            {
                if (fullNameBuilder.Length > 0) fullNameBuilder.Append('\\');
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

        /// <summary>
        ///     Prepare ETW parser
        /// </summary>
        /// <param name="traceSessionSource">
        ///     WTW trace event source to listen
        /// </param>
        private void MakeKernelParserStateless(ETWTraceEventSource traceSessionSource)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(traceSessionSource);

                const KernelTraceEventParser.ParserTrackingOptions options = KernelTraceEventParser.ParserTrackingOptions.None;
                var kernelParser = new KernelTraceEventParser(traceSessionSource, options);

                var kernelField = typeof(ETWTraceEventSource).GetField("_Kernel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (kernelField == null)
                    throw new MissingFieldException(typeof(ETWTraceEventSource).FullName, "_Kernel");
                kernelField.SetValue(traceSessionSource, kernelParser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Kernel parser stateless configuration.");
            }
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
                    var change = new RegistryChange(ev, keyName);
                    change.ScopeHash = configuration.ScopeHash;

                    _messageStore.Add(change);
                }
            }
            catch (Exception ex)
            {
                ex.Log(_logger);
            }
        }

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
                    lock (_sessionLock) session = _session;
                    session?.Stop();
                }

                _disposedValue = true;
            }
        }

        #endregion Dispose
    }
}
