using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using WinFIMLog.Utils;

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
                                             KernelTraceEventParser.Keywords.FileIOInit);
                session.Source.Kernel.All += Record;
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

            var attribution = new Attribution
            {
                ProcessID = data.ProcessID,
                ProcessName = data.ProcessName ?? string.Empty,
                RecordedAt = DateTime.UtcNow
            };

            try
            {
                using var process = Process.GetProcessById(data.ProcessID);
                attribution.ProcessName = process.ProcessName;
                var userInfo = SidUserInfoCache.Get(process);
                attribution.Username = userInfo.Username;
                attribution.UserSID = userInfo.SID;
            }
            catch (Exception)
            {
                // The process may have exited between the kernel event and owner lookup.
            }

            _attributions[path] = attribution;
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
            internal string ProcessName { get; set; } = string.Empty;
            internal DateTime RecordedAt { get; init; }
            internal string? Username { get; set; }
            internal string? UserSID { get; set; }
        }
    }
}
