using System;

namespace WinFIMLog.FIM
{
    public interface IChange
    {
        string ACLs { get; set; }

        string AttributionConfidence { get; set; }
        string AttributionMethod { get; set; }
        string? AttributionMissingReason { get; set; }
        DateTimeOffset? AttributionSourceTimestamp { get; set; }
        AttributionStatus AttributionStatus { get; set; }
        ChangeCategory ChangeCategory { get; set; }
        ConfigChangeType ConfigChangeType { get; set; }
        DateTime DateTime { get; set; }
        string Entity { get; set; }
        string Id { get; set; }
        int? ProcessID { get; set; }
        string? ProcessName { get; set; }
        ulong? ProcessSequenceNumber { get; set; }
        string ScopeHash { get; set; }
        string SourceComputer { get; set; }
        string? Username { get; set; }

        string? UserSID { get; set; }
    }
}
