using System;

namespace WinFIMLog.FIM
{
    public class Change : IChange
    {
        public string ACLs { get; set; }

        /// <summary>ACL evidence from the previously projected state, when available.</summary>
        public string PreviousACL { get; set; } = string.Empty;

        /// <summary>Consumer-facing confidence in the optional attribution.</summary>
        public string AttributionConfidence { get; set; } = "None";

        /// <summary>Technique used to obtain the optional identity evidence.</summary>
        public string AttributionMethod { get; set; } = "None";

        /// <summary>Machine-readable explanation when attribution is incomplete.</summary>
        public string? AttributionMissingReason { get; set; }

        /// <summary>Timestamp of the source event used for correlation.</summary>
        public DateTimeOffset? AttributionSourceTimestamp { get; set; }

        public AttributionStatus AttributionStatus { get; set; } = AttributionStatus.Unattributed;
        public ChangeCategory ChangeCategory { get; set; }

        public ConfigChangeType ConfigChangeType { get; set; }

        public DateTime DateTime { get; set; }

        public string Entity { get; set; }

        public string Id { get; set; }

        public int? ProcessID { get; set; }

        public string? ProcessName { get; set; }

        /// <summary>Kernel process sequence number; unlike PID, this identifies an instance.</summary>
        public ulong? ProcessSequenceNumber { get; set; }

        /// <summary>Canonical identity of the effective monitoring scope.</summary>
        public string ScopeHash { get; set; } = string.Empty;

        public string SourceComputer { get; set; }
        public string? Username { get; set; }

        public string? UserSID { get; set; }
    }
}
