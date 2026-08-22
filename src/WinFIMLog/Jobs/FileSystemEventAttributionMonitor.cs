using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using WinFIMLog.FIM;
using WinFIMLog.Utils;

namespace WinFIMLog.Jobs
{
    /// <summary>
    /// Correlates FileSystemWatcher notifications with the kernel FileIO event which caused them.
    /// FileSystemWatcher does not expose a process or security principal itself.
    /// </summary>
    internal sealed class FileSystemEventAttributionMonitor : IDisposable
    {
        // ETW sessions are machine-wide and survive an unclean process exit. Keep this name
        // stable so TraceEvent's create semantics replace an orphan instead of allocating a new
        // kernel logger on every service restart.
        internal static readonly string SessionName = "WinFIMLog-FileIO";

        private const string LegacySessionNamePrefix = "WinFIMLog-FileIO-";
        private static readonly TimeSpan AttributionLifetime = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<string, Attribution> _attributions =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ILogger _logger;
        private readonly ProcessInstanceStore _processes = new();
        private readonly int _serviceProcessId = Environment.ProcessId;
        private readonly Settings _settings;
        private readonly ManualResetEventSlim _started = new(initialState: false);
        private TraceEventSession? _session;
        private Thread? _thread;

        internal FileSystemEventAttributionMonitor(ILogger logger, Settings settings)
        {
            _logger = logger;
            _settings = settings;
        }

        public void Dispose()
        {
            _session?.Stop();
            _thread?.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            _started.Dispose();
        }

        internal static bool IsLegacySessionName(string sessionName)
        {
            if (!sessionName.StartsWith(LegacySessionNamePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(sessionName.AsSpan(LegacySessionNamePrefix.Length), System.Globalization.CultureInfo.InvariantCulture, out var processId) &&
                   processId > 0;
        }

        internal void Start()
        {
            if (_thread is not null)
            {
                return;
            }

            _thread = new Thread(Monitor)
            {
                IsBackground = true,
                Name = "File system ETW attribution",
            };
            _thread.Start();
            _started.Wait(TimeSpan.FromSeconds(2));
        }

        internal bool TryGet(string path, out Attribution attribution)
        {
            if (_attributions.TryGetValue(path, out attribution!) &&
                DateTime.UtcNow - attribution.RecordedAt <= AttributionLifetime)
            {
                return true;
            }

            _attributions.TryRemove(path, out _);
            attribution = default;
            return false;
        }

        private static bool IsMutation(string opcodeName) => opcodeName is
            "Create" or "Write" or "Delete" or "Rename" or "SetInfo" or "SetInformation";

        private static ulong? ReadSequence(TraceEvent data)
        {
            var value = data.PayloadByName("ProcessSequenceNumber");
            if (value is null)
            {
                return null;
            }

            try { return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
        }

        private void EndProcess(ProcessTraceData data)
        {
            var sequence = ReadSequence(data);
            if (sequence is not null)
            {
                _processes.End(data.ProcessID, sequence.Value);
            }
        }

        private void Monitor()
        {
            try
            {
                RemoveLegacySessions();
                using var session = new TraceEventSession(SessionName, fileName: null);
                _session = session;
                _started.Set();
                session.StopOnDispose = true;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.FileIO |
                                             KernelTraceEventParser.Keywords.FileIOInit |
                                             KernelTraceEventParser.Keywords.Process);
                session.Source.Kernel.ProcessStart += RecordProcess;
                session.Source.Kernel.ProcessDCStart += RecordProcess;
                session.Source.Kernel.ProcessStop += EndProcess;
                session.Source.Kernel.All += Record;
                // Enabling the kernel Process keyword supplies Process/DCStart rundown events
                // for processes which already exist. Do not call CaptureState here: that API
                // sends a user-mode provider control command, which the kernel system provider
                // rejects with E_INVALIDARG.
                session.Source.Process();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "File system user attribution through ETW is unavailable.");
            }
            finally
            {
                _started.Set();
                _session = null;
            }
        }

        private void Record(TraceEvent data)
        {
            if (data.ProcessID <= 0 || data.ProcessID == _serviceProcessId ||
                !string.Equals(data.TaskName, "FileIO", StringComparison.OrdinalIgnoreCase) ||
                !IsMutation(data.OpcodeName))
            {
                return;
            }

            var path = data.PayloadByName("FileName") as string;
            var configuration = _settings.Capture();
            if (string.IsNullOrWhiteSpace(path) || !configuration.IsMonitoredPath(path))
            {
                return;
            }

            var sequence = ReadSequence(data);
            if (sequence is null)
            {
                _attributions[path] = Attribution.Missing(data.ProcessID, data.TimeStamp,
                    _processes.RundownComplete ? "ProcessSequenceNumberMissing" : "ProcessRundownMissing");
                return;
            }

            if (_processes.TryResolve(data.ProcessID, sequence.Value, out var processEvidence))
            {
                _attributions[path] = new Attribution(data.ProcessID, sequence,
                    processEvidence.ProcessName, DateTime.UtcNow, new DateTimeOffset(data.TimeStamp),
                    processEvidence.Username, processEvidence.UserSid, AttributionStatus.Attributed, MissingReason: null);
                return;
            }

            _attributions[path] = new Attribution(data.ProcessID, sequence,
                data.ProcessName ?? string.Empty, DateTime.UtcNow, new DateTimeOffset(data.TimeStamp),
Username: null, UserSID: null, AttributionStatus.Unavailable, "ProcessInstanceNotFound");
        }

        private void RecordProcess(ProcessTraceData data)
        {
            var sequence = ReadSequence(data);
            if (sequence is null)
            {
                return;
            }

            string? username = null;
            string? sid = null;
            try
            {
                using var process = Process.GetProcessById(data.ProcessID);
                var user = SidUserInfoCache.Get(process);
                username = user.Username;
                sid = user.SID;
            }
            catch (Exception) { /* The kernel identity remains useful after process exit/access denial. */ }

            _processes.Record(new ProcessInstanceEvidence(data.ProcessID, sequence.Value,
                data.ProcessName ?? string.Empty, new DateTimeOffset(data.TimeStamp), username, sid));
            if (string.Equals(data.OpcodeName, "DCStart", StringComparison.OrdinalIgnoreCase))
            {
                _processes.MarkRundownComplete();
            }
        }

        private void RemoveLegacySessions()
        {
            // Versions which included the process ID in the session name could strand one
            // kernel logger per crash. Reclaim only that application-owned legacy namespace.
            foreach (var activeSessionName in TraceEventSession.GetActiveSessionNames())
            {
                if (!IsLegacySessionName(activeSessionName))
                {
                    continue;
                }

                try
                {
                    using var legacySession = TraceEventSession.GetActiveSession(activeSessionName);
                    legacySession?.Stop();
                    _logger.LogInformation("Removed orphaned file attribution ETW session {SessionName}.",
                        activeSessionName);
                }
                catch (Exception ex)
                {
                    // Failure to remove one logger should not prevent the stable session from
                    // replacing its own orphan or obscure the actual startup failure.
                    _logger.LogWarning(ex, "Could not remove legacy file attribution ETW session {SessionName}.",
                        activeSessionName);
                }
            }
        }

        // TraceEventSource is already a synchronous, forward-only stream. Keeping its small
        // projection as a value in the coalescing dictionary avoids allocating one wrapper object
        // per FileIO event; adding a Channel/IAsyncEnumerable stage would instead retain events
        // and allocate queue nodes or async state while providing no additional backpressure to ETW.
        internal readonly record struct Attribution(
            int ProcessID,
            ulong? ProcessSequenceNumber,
            string ProcessName,
            DateTime RecordedAt,
            DateTimeOffset SourceTimestamp,
            string? Username,
            string? UserSID,
            AttributionStatus Status,
            string? MissingReason)
        {
            internal static Attribution Missing(int processId, DateTime timestamp, string reason) =>
                new(processId, ProcessSequenceNumber: null, string.Empty, DateTime.UtcNow, new DateTimeOffset(timestamp),
Username: null, UserSID: null,
string.Equals(reason, "ProcessRundownMissing", StringComparison.OrdinalIgnoreCase) ? AttributionStatus.RundownMissing : AttributionStatus.Unavailable,
                    reason);
        }
    }
}
