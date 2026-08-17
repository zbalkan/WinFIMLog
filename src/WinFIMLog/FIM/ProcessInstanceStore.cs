using System;
using System.Collections.Concurrent;

namespace WinFIMLog.FIM
{
    /// <summary>
    /// Reuse-safe process-instance index populated by kernel process start and rundown events.
    /// A PID is deliberately never accepted as an identity on its own.
    /// </summary>
    public sealed class ProcessInstanceStore
    {
        private readonly ConcurrentDictionary<(int Pid, ulong Sequence), ProcessInstanceEvidence> instances = new();

        public bool RundownComplete { get; private set; }

        public void End(int processId, ulong processSequenceNumber) =>
            instances.TryRemove((processId, processSequenceNumber), out _);

        public void MarkRundownComplete() => RundownComplete = true;

        public void Record(ProcessInstanceEvidence evidence) =>
                            instances[(evidence.ProcessId, evidence.ProcessSequenceNumber)] = evidence;

        public bool TryResolve(int processId, ulong processSequenceNumber, out ProcessInstanceEvidence evidence) =>
            instances.TryGetValue((processId, processSequenceNumber), out evidence!);
    }

    public sealed record ProcessInstanceEvidence(
        int ProcessId,
        ulong ProcessSequenceNumber,
        string ProcessName,
        DateTimeOffset SourceTimestamp,
        string? Username = null,
        string? UserSid = null);
}
