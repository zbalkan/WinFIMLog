using System;
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

        /// <summary>
        /// Gets a human-readable string representation of the reason flags bitmap.
        /// </summary>
        /// <param name="reasonFlags">The USN reason flags bitmap</param>
        /// <returns>Comma-separated string of reason flag names</returns>
        public static string FormatReasonFlags(uint reasonFlags)
        {
            var reasons = new System.Collections.Generic.List<string>(8);

            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.DataOverwrite) != 0)
                reasons.Add("DataOverwrite");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.DataExtend) != 0)
                reasons.Add("DataExtend");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.DataTruncation) != 0)
                reasons.Add("DataTruncation");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.NameChange) != 0)
                reasons.Add("NameChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.NameChangeNew) != 0)
                reasons.Add("NameChangeNew");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.BasicInfoChange) != 0)
                reasons.Add("BasicInfoChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.SecurityChange) != 0)
                reasons.Add("SecurityChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.FileCreate) != 0)
                reasons.Add("FileCreate");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.FileDelete) != 0)
                reasons.Add("FileDelete");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.RenameOldName) != 0)
                reasons.Add("RenameOldName");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.RenameNewName) != 0)
                reasons.Add("RenameNewName");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.IntegrityChange) != 0)
                reasons.Add("IntegrityChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.ObjectIdChange) != 0)
                reasons.Add("ObjectIdChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.ReparsePointChange) != 0)
                reasons.Add("ReparsePointChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.CompressionChange) != 0)
                reasons.Add("CompressionChange");
            if ((reasonFlags & (uint)NativeMethods.UsnReasonFlags.EncryptionChange) != 0)
                reasons.Add("EncryptionChange");

            return reasons.Count == 0 ? "(no flags)" : string.Join(" | ", reasons);
        }
    }
}
