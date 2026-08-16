using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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

        private readonly CancellationTokenSource _cancellationTokenSource;

        private readonly ILogger _logger;

        private readonly IBuffer<RegistryChange> _messageStore;

        private readonly int _pid;

        private readonly Settings _settings;

        private readonly ObjectPool<StringBuilder> _sbPool = new DefaultObjectPoolProvider().CreateStringBuilderPool();

        private readonly object _sessionLock = new();

        private TraceEventSession? _session;

        private bool _disposedValue;

        public RegistryMonitorJob(ILogger logger, IBuffer<RegistryChange> regStore, Settings settings)
        {
            _logger = logger;
            _pid = Environment.ProcessId;
            _cancellationTokenSource = new CancellationTokenSource();
            _messageStore = regStore;
            _settings = settings;
        }

        /// <summary>
        ///     Start monitoring selected Registry keys
        /// </summary>
        /// <exception cref="FieldAccessException">
        /// </exception>
        /// <exception cref="TargetException">
        /// </exception>
        public void Start()
        {
            // No baseline database for registry keys
            CleanupExistingSession();

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            using var session = CreateSession();
            lock (_sessionLock)
            {
                _session = session;
            }

            try
            {
                using var cancellationRegistration = _cancellationTokenSource.Token.Register(() => session.Stop());
                _logger.LogInformation("Started ETW session '{SessionName}' for Registry changes.", ETWSessionName);
                session.Source.Process();

                if (session.Source.EventsLost > 0)
                {
                    _logger.LogWarning(
                        "ETW session '{SessionName}' lost {EventsLost} events. Consider reducing the monitored registry scope.",
                        ETWSessionName,
                        session.Source.EventsLost);
                }
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
            field?.SetValue(session, SessionBufferQuantumKilobytes);
        }

        private static void UpdateCache(RegistryTraceData data) =>
            Cached<string>.Save(data.KeyHandle, data.KeyName, TimeSpan.FromHours(1));

        /// <summary>
        ///     Stop monitoring selected Registry keys
        /// </summary>
        /// <exception cref="AggregateException">
        /// </exception>
        public void Stop()
        {
            _cancellationTokenSource.Cancel();

            TraceEventSession? session;
            lock (_sessionLock)
            {
                session = _session;
            }

            session?.Stop();
        }

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

        private bool IsMonitoredEvent(string keyName, int pid)
        {
            if (pid == _pid || pid == -1)
            {
                return false;
            }

            if (string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            return _settings.IsMonitoredKey(keyName);
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
                kernelField?.SetValue(traceSessionSource, kernelParser);
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

                if (IsMonitoredEvent(keyName, ev.ProcessID))
                {
                    Debug.WriteLine($"Processing event: {ev.EventName} for {keyName}");
                    var change = new RegistryChange(ev, keyName);

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
                    Stop();
                    _cancellationTokenSource.Dispose();
                }

                _disposedValue = true;
            }
        }

        #endregion Dispose
    }
}
