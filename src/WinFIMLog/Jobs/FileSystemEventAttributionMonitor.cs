using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using WinFIMLog.Utils;
using WinFIMLog.FIM;

namespace WinFIMLog.Jobs
{
    /// <summary>
    /// Correlates FileSystemWatcher notifications with the kernel FileIO event which caused them.
    /// FileSystemWatcher does not expose a process or security principal itself.
    /// </summary>
    internal sealed class FileSystemEventAttributionMonitor : IDisposable
    {
        private const string SessionNamePrefix = "WinFIMLog-FileIO-";
        private static readonly TimeSpan AttributionLifetime = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<string, Attribution> _attributions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;
        private readonly int _serviceProcessId = Environment.ProcessId;
        private readonly Settings _settings;
        private readonly string _sessionName;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly ProcessInstanceStore _processes = new();
        private TraceEventSession? _session;
        private Thread? _thread;

        internal FileSystemEventAttributionMonitor(ILogger logger, Settings settings)
        {
            _logger = logger;
            _settings = settings;
            _sessionName = SessionNamePrefix + _serviceProcessId;
        }

        internal void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(Monitor)
            {
                IsBackground = true,
                Name = "File system ETW attribution"
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
            attribution = null!;
            return false;
        }

        private void Monitor()
        {
            try
            {
                using var session = new TraceEventSession(_sessionName, null);
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
                // Existing processes must be captured before file events are trusted. Without
                // rundown a PID could be joined to a later, unrelated process instance.
                session.CaptureState(KernelTraceEventParser.ProviderGuid);
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
            if (string.IsNullOrWhiteSpace(path) || !_settings.IsMonitoredPath(path)) return;

            var sequence = ReadSequence(data);
            if (sequence == null)
            {
                _attributions[path] = Attribution.Missing(data.ProcessID, data.TimeStamp,
                    _processes.RundownComplete ? "ProcessSequenceNumberMissing" : "ProcessRundownMissing");
                return;
            }

            var attribution = new Attribution
            {
                ProcessID = data.ProcessID,
                ProcessName = data.ProcessName ?? string.Empty,
                ProcessSequenceNumber = sequence,
                RecordedAt = DateTime.UtcNow,
                SourceTimestamp = new DateTimeOffset(data.TimeStamp),
                Status = AttributionStatus.Unavailable,
                MissingReason = "ProcessInstanceNotFound"
            };

            if (_processes.TryResolve(data.ProcessID, sequence.Value, out var processEvidence))
            {
                attribution.ProcessName = processEvidence.ProcessName;
                attribution.Username = processEvidence.Username;
                attribution.UserSID = processEvidence.UserSid;
                attribution.Status = AttributionStatus.Attributed;
                attribution.MissingReason = null;
            }

            _attributions[path] = attribution;
        }

        private void RecordProcess(ProcessTraceData data)
        {
            var sequence = ReadSequence(data);
            if (sequence == null) return;

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
                _processes.MarkRundownComplete();
        }

        private void EndProcess(ProcessTraceData data)
        {
            var sequence = ReadSequence(data);
            if (sequence != null) _processes.End(data.ProcessID, sequence.Value);
        }

        private static ulong? ReadSequence(TraceEvent data)
        {
            var value = data.PayloadByName("ProcessSequenceNumber");
            if (value == null) return null;
            try { return Convert.ToUInt64(value); }
            catch (Exception) { return null; }
        }

        private static bool IsMutation(string opcodeName) => opcodeName is
            "Create" or "Write" or "Delete" or "Rename" or "SetInfo" or "SetInformation";

        public void Dispose()
        {
            _session?.Stop();
            _thread?.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            _started.Dispose();
        }

        internal sealed class Attribution
        {
            internal int ProcessID { get; init; }
            internal ulong? ProcessSequenceNumber { get; init; }
            internal string ProcessName { get; set; } = string.Empty;
            internal DateTime RecordedAt { get; init; }
            internal DateTimeOffset SourceTimestamp { get; init; }
            internal string? Username { get; set; }
            internal string? UserSID { get; set; }
            internal AttributionStatus Status { get; set; }
            internal string? MissingReason { get; set; }

            internal static Attribution Missing(int processId, DateTime timestamp, string reason) => new()
            {
                ProcessID = processId,
                RecordedAt = DateTime.UtcNow,
                SourceTimestamp = new DateTimeOffset(timestamp),
                Status = reason == "ProcessRundownMissing" ? AttributionStatus.RundownMissing : AttributionStatus.Unavailable,
                MissingReason = reason
            };
        }
    }
}
