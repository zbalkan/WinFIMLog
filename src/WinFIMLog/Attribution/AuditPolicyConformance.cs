using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinFIMLog.Attribution
{
    public interface IAuditPolicyConformance
    {
        bool IsEnabled(Guid subcategory, out string reason);
    }

    internal sealed class WindowsAuditPolicyConformance : IAuditPolicyConformance
    {
        public static readonly Guid FileSystemSubcategory = new("0CCE921D-69AE-11D9-BED3-505054503030");
        public static readonly Guid RegistrySubcategory = new("0CCE9228-69AE-11D9-BED3-505054503030");

        public bool IsEnabled(Guid subcategory, out string reason)
        {
            var categories = new[] { subcategory };
            if (!AuditQuerySystemPolicy(categories, 1, out var buffer))
            {
                reason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            try
            {
                var policy = Marshal.PtrToStructure<AuditPolicyInformation>(buffer);
                // SUCCESS (1) or FAILURE (2) auditing satisfies the attribution dependency.
                var enabled = (policy.AuditingInformation & 3) != 0;
                reason = enabled ? string.Empty : "Success and failure auditing are both disabled.";
                return enabled;
            }
            finally { AuditFree(buffer); }
        }

        [DllImport("advapi32.dll")]
        private static extern void AuditFree(IntPtr buffer);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AuditQuerySystemPolicy([In] Guid[] subCategoryGuids,
            uint policyCount, out IntPtr auditPolicy);

        [StructLayout(LayoutKind.Sequential)]
        private struct AuditPolicyInformation
        {
            public Guid AuditSubCategoryGuid;
            public uint AuditingInformation;
            public Guid AuditCategoryGuid;
        }
    }
}
