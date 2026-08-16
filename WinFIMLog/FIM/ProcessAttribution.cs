using System;

namespace WinFIMLog.FIM
{
    public readonly record struct ProcessAttributionResult(
        AttributionStatus Status, string? ProcessName, string? Username, string? UserSid);

    public static class ProcessAttribution
    {
        public static ProcessAttributionResult Resolve(int processId, string? reportedProcessName,
            Func<int, (string ProcessName, string? Username, string? UserSid)> lookup)
        {
            try
            {
                var result = lookup(processId);
                return new ProcessAttributionResult(AttributionStatus.Attributed, result.ProcessName,
                    result.Username, result.UserSid);
            }
            catch
            {
                return new ProcessAttributionResult(AttributionStatus.Unavailable, reportedProcessName, null, null);
            }
        }
    }
}
