using System.Collections.Generic;

namespace WinFIMLog.IO.Security
{
    public class AccessControlList
    {
        public string Owner { get; set; }

        public List<AccessControlEntry> Permissions { get; set; }

        public string? PrimaryGroupOfOwner { get; set; }
    }
}
