using System;

namespace WinFIMLog.FIM
{
    public class Change : IChange
    {
        public string ACLs { get; set; }

        public AttributionStatus AttributionStatus { get; set; } = AttributionStatus.Unattributed;

        public ChangeCategory ChangeCategory { get; set; }

        public ConfigChangeType ConfigChangeType { get; set; }

        public DateTime DateTime { get; set; }

        public string Entity { get; set; }

        public string Id { get; set; }

        public int? ProcessID { get; set; }

        public string? ProcessName { get; set; }

        public string SourceComputer { get; set; }

        public string? Username { get; set; }

        public string? UserSID { get; set; }
    }
}
