using System;
using System.Linq;
using WinFIMLog.FIM;
using WinFIMLog.Utils;

namespace WinFIMLog.USN
{
    /// <summary>Maps USN reason flags to WinFIMLog change categories using priority-based classification.</summary>
    /// <remarks>
    /// Each USN record can have multiple reason flags set. This mapper applies a priority order:
    /// 1. Delete (0x200) - file deletion
    /// 2. Create (0x100) - file creation
    /// 3. Rename (0x1000 | 0x2000) - file rename
    /// 4. SecurityChange (0x80) - permission/ACL change
    /// 5. Modify (0x1 | 0x2 | 0x4 | 0x40) - data/attribute changes
    /// 6. Other - unclassified changes
    ///
    /// This ensures exactly one change category per USN record for deterministic event emission.
    /// The full reason bitmap is retained as metadata for downstream analysis.
    /// </remarks>
    public static class UsnReasonMapper
    {
        /// <summary>Maps a USN reason flags bitmap to the primary ChangeCategory.</summary>
        /// <param name="reasonFlags">The USN reason flags bitmap from USN_RECORD.Reason</param>
        /// <returns>The primary ChangeCategory for this USN record</returns>
        public static ChangeCategory MapReasonToChangeCategory(uint reasonFlags)
        {
            // Priority 1: File deletion (most critical)
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.FileDelete) != 0)
                return ChangeCategory.Deleted;

            // Priority 2: File creation
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.FileCreate) != 0)
                return ChangeCategory.Created;

            // Priority 3: Rename (represented as delete + create pattern in change logs)
            if ((reasonFlags & ((uint)NativeMethods.UsnReasonFlags.RenameOldName | (uint)NativeMethods.UsnReasonFlags.RenameNewName)) != 0)
                return ChangeCategory.Created;  // Rename events are handled as create in WinFIMLog

            // Priority 4: Security/permission changes - map to Changed
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.SecurityChange) != 0)
                return ChangeCategory.Changed;

            // Priority 5: Data modifications (overwrite, extend, truncate, basic info change, etc.)
            uint modifyFlags = (uint)(NativeMethods.UsnReasonFlags.DataOverwrite |
                                      NativeMethods.UsnReasonFlags.DataExtend |
                                      NativeMethods.UsnReasonFlags.DataTruncation |
                                      NativeMethods.UsnReasonFlags.BasicInfoChange);

            if ((reasonFlags & modifyFlags) != 0)
                return ChangeCategory.Changed;

            // Priority 6: Other changes (attributes, reparse points, compression, etc.)
            if (reasonFlags != 0)
                return ChangeCategory.Changed;  // Fallback for unhandled flags

            // Edge case: no flags set (shouldn't happen in practice)
            return ChangeCategory.Changed;
        }

        /// <summary>Names every reason flag set on a record.</summary>
        /// <remarks>
        /// Retained alongside the category because the priority rules above deliberately collapse a
        /// multi-reason record to one value, and the discarded detail is what tells an analyst
        /// whether a delete followed a create or a plain overwrite.
        /// </remarks>
        public static string FormatReasonFlags(uint reasonFlags)
        {
            if (reasonFlags == 0)
            {
                return "(no flags)";
            }

            var names = Enum.GetValues<NativeMethods.UsnReasonFlags>()
                .Where(flag => (reasonFlags & (uint)flag) != 0)
                .Select(flag => flag.ToString());

            return string.Join(" | ", names);
        }
    }
}
