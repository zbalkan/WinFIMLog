using System;

namespace WinFIMLog.FIM
{
    public class Change : IChange
    {
        public string ACLs { get; set; }

        public ChangeCategory ChangeCategory { get; set; }

        public ConfigChangeType ConfigChangeType { get; set; }

        public DateTime DateTime { get; set; }

        public string Entity { get; set; }

        public string Id { get; set; }

        public string SourceComputer { get; set; }
    }
}