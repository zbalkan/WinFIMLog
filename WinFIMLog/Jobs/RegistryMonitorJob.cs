using System;
using System.Collections.Generic;
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

        private const double MonitorTimeInSeconds = 0.2;

        private const NtKeywords TraceFlags = NtKeywords.Registry;

        private readonly CancellationTokenSource _cancellationTokenSource;

        private readonly List<RegistryChange> _changes;

        private readonly ILogger _logger;

        private readonly IBuffer<RegistryChange> _messageStore;

        private readonly int _pid;

        private readonly ObjectPool<StringBuilder> _sbPool = new DefaultObjectPoolProvider().CreateStringBuilderPool();

        private bool _disposedValue;

        public RegistryMonitorJob(ILogger logger, IBuffer<RegistryChange> regStore)
        {
            _logger = logger;
            _pid = Environment.ProcessId;
            _cancellationTokenSource = new CancellationTokenSource();
            _messageStore = regStore;
            _changes = new List<RegistryChange>();
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

            _logger.LogInformation("Started ETW session 'RegistryWatcher' for Registry changes.");

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var session = new TraceEventSession(ETWSessionName, null);
                session.EnableKernelProvider(TraceFlags);
                MakeKernelParserStateless(session.Source);

                session.Source.Kernel.RegistryKCBRundownEnd += UpdateCache;

                session.Source.Kernel.RegistryCreate += ProcessEvent;
                session.Source.Kernel.RegistryDelete += ProcessEvent;
                session.Source.Kernel.RegistrySetValue += ProcessEvent;
                session.Source.Kernel.RegistryDeleteValue += ProcessEvent;
                session.Source.Kernel.RegistrySetInformation += ProcessEvent;

                using (session)
                {
                    var timer = new Timer((object? _) => session!.Stop(), null, (int)(MonitorTimeInSeconds * 1000), Timeout.Infinite);
                    session.Source.Process();
                }

                try
                {
                    _messageStore.AddRange(_changes);
                    _changes.Clear();
                }
                catch (Exception ex)
                {
                    ex.Log(_logger);
                }

                session.Stop();
                session.Dispose();
            }
        }

        private void UpdateCache(RegistryTraceData data) => Cached<string>.Save(data.KeyHandle, data.KeyName, TimeSpan.FromSeconds(MonitorTimeInSeconds * 2));

        /// <summary>
        ///     Stop monitoring selected Registry keys
        /// </summary>
        /// <exception cref="AggregateException">
        /// </exception>
        public void Stop() => _cancellationTokenSource.Cancel();

        private void CleanupExistingSession()
        {
            try
            {
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(ETWSessionName))
                {
                    using var session = new TraceEventSession(ETWSessionName);
                    session.Stop();
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

            return Settings.Instance.IsMonitoredKey(keyName);
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

                var t = traceSessionSource.GetType();
                var kernelField = t.GetField("_Kernel", BindingFlags.Instance | BindingFlags.SetField | BindingFlags.NonPublic);
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

                    _changes.Add(change);
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
                    _cancellationTokenSource.Dispose();
                }

                _disposedValue = true;
            }
        }

        #endregion Dispose
    }
}