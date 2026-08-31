using System;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace WinFIMLog.Utils
{
    internal static partial class NativeMethods
    {
        private enum KEY_INFORMATION_CLASS
        {
            KeyBasicInformation,            // A KEY_BASIC_INFORMATION structure is supplied.

            KeyNodeInformation,             // A KEY_NODE_INFORMATION structure is supplied.

            KeyFullInformation,             // A KEY_FULL_INFORMATION structure is supplied.

            KeyNameInformation,             // A KEY_NAME_INFORMATION structure is supplied.

            KeyCachedInformation,           // A KEY_CACHED_INFORMATION structure is supplied.

            KeyFlagsInformation,            // Reserved for system use.

            KeyVirtualizationInformation,   // A KEY_VIRTUALIZATION_INFORMATION structure is supplied.

            KeyHandleTagsInformation,       // Reserved for system use.

            MaxKeyInfoClass                 // The maximum value in this enumeration type.
        }

        [LibraryImport("advapi32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int OpenProcessToken(IntPtr processHandle, // handle to process
            int desiredAccess, // desired access to process
            ref IntPtr tokenHandle // handle to open access token
        );

        [StructLayout(LayoutKind.Sequential)]
        public struct KEY_NAME_INFORMATION
        {
            public uint NameLength;     // The size, in bytes, of the key name string in the Name array.

            public char[] Name;           // An array of wide characters that contains the name of the key.

            // This character string is not null-terminated. Only the first element in this array is
            // included in the KEY_NAME_INFORMATION structure definition. The storage for the
            // remaining elements in the array immediately follows this element.
        }

        // ========== USN Journal / Change Journal Constants and Structures ==========

        /// <summary>IOCTL code for reading NTFS Change Journal (USN Journal)</summary>
        public const uint FSCTL_READ_USN_JOURNAL = 0x000900B3;

        /// <summary>IOCTL code for querying USN Journal data</summary>
        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;

        /// <summary>USN Reason Flags (bitmap)</summary>
        [Flags]
        public enum UsnReasonFlags : uint
        {
            DataOverwrite = 0x00000001,
            DataExtend = 0x00000002,
            DataTruncation = 0x00000004,
            NameChange = 0x00000010,
            NameChangeNew = 0x00000020,
            BasicInfoChange = 0x00000040,
            SecurityChange = 0x00000080,
            FileCreate = 0x00000100,
            FileDelete = 0x00000200,
            RenameOldName = 0x00001000,
            RenameNewName = 0x00002000,
            IntegrityChange = 0x00004000,
            ObjectIdChange = 0x00008000,
            ReparsePointChange = 0x00010000,
            CompressionChange = 0x00020000,
            EncryptionChange = 0x00040000,
            ObjectIdChange2 = 0x00080000,
            TxnScopeChange = 0x00100000,
            FsctlChange = 0x00200000,
            InformationChange = 0x00400000,
            HardLinkChange = 0x00800000,
            CompressionChange2 = 0x01000000,
            OfflineChange = 0x02000000,
            FirstChange = 0x04000000
        }

        /// <summary>USN_RECORD_V2 structure (NTFS 3.0+, 64-bit file references)</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct UsnRecord
        {
            public uint RecordLength;
            public ushort MajorVersion;
            public ushort MinorVersion;
            public ulong FileReferenceNumber;
            public ulong ParentDirectoryReferenceNumber;
            public long Usn;
            public long TimeStamp;  // LARGE_INTEGER (100-nanosecond intervals since 1/1/1601)
            public uint Reason;
            public uint SourceInfo;
            public uint SecurityId;
            public uint FileAttributes;

            // WORD in the Win32 definition, not DWORD. Widening these shifts the filename fields and
            // the struct size, which silently misparses every record.
            public ushort FileNameLength;

            public ushort FileNameOffset;
            // Variable-length filename follows (UTF-16LE)
        }

        /// <summary>Byte length of USN_RECORD_V2 up to, but excluding, the filename.</summary>
        public const int UsnRecordV2HeaderBytes = 60;

        /// <summary>READ_USN_JOURNAL_DATA_V1 structure for FSCTL_READ_USN_JOURNAL</summary>
        /// <remarks>
        /// <c>UsnJournalID</c> is mandatory: the driver rejects a read that does not name the journal
        /// it is reading, which is what makes journal recreation detectable rather than silent.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct ReadUsnJournalData
        {
            public long StartUsn;
            public uint ReasonMask;
            public uint ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public ulong UsnJournalID;
            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        /// <summary>USN_JOURNAL_DATA structure returned by FSCTL_QUERY_USN_JOURNAL</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct UsnJournalData
        {
            public ulong UsnJournalID;
            public long FirstUsn;
            public long NextUsn;
            public long LowestValidUsn;
            public long MaxUsn;
            public ulong MaximumSize;
            public ulong AllocationDelta;
        }

        /// <summary>FILE_ID_TYPE: which union member <see cref="FileIdDescriptor"/> carries.</summary>
        /// <remarks>This codebase only ever sets <see cref="FileId"/>, per ADR-0021.</remarks>
        public const uint FileId = 0;

        /// <summary>
        /// FILE_ID_DESCRIPTOR, the actual parameter OpenFileById takes.
        /// </summary>
        /// <remarks>
        /// The real struct is <c>{ DWORD dwSize; FILE_ID_TYPE Type; union { LARGE_INTEGER FileId;
        /// GUID ObjectId; FILE_ID_128 ExtendedFileId; }; }</c> — 24 bytes, not the bare 64-bit value
        /// a caller might expect. A prior version of this struct modeled only 16 bytes with no
        /// discriminator, which does not describe any layout OpenFileById actually reads; see
        /// ADR-0021 for how that surfaced and why this codebase only ever populates the 64-bit
        /// <see cref="FileIdValue"/> member (Type = <see cref="FileId"/>), never an Object ID.
        /// <see cref="FileIdDescriptorTests"/> pins the offsets and total size against this contract.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct FileIdDescriptor
        {
            public uint Size;
            public uint Type;
            public long FileIdValue;

            /// <summary>Unused tail of the union (GUID/FILE_ID_128 are 16 bytes; FileId is 8).</summary>
            private readonly long reserved;
        }

        /// <summary>Invokes FSCTL_READ_USN_JOURNAL or FSCTL_QUERY_USN_JOURNAL on a volume</summary>
        [DllImport("kernel32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            ref ReadUsnJournalData lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        /// <summary>Overload for querying journal metadata</summary>
        [DllImport("kernel32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            out UsnJournalData lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        /// <summary>Opens a file or directory (for volume handle)</summary>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode), SuppressUnmanagedCodeSecurity]
        public static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        /// <summary>Gets the final path name for a file handle</summary>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode), SuppressUnmanagedCodeSecurity]
        public static extern uint GetFinalPathNameByHandleW(
            IntPtr hFile,
            [Out] char[] lpszFilePath,
            uint cchFilePath,
            uint dwFlags
        );

        /// <summary>Creates a file or opens an existing file by file ID (Windows Vista+)</summary>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode), SuppressUnmanagedCodeSecurity]
        public static extern IntPtr OpenFileById(
            IntPtr hVolumeHandle,
            ref FileIdDescriptor lpFileID,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwFlagsAndAttributes
        );

        /// <summary>Closes a file or volume handle</summary>
        [DllImport("kernel32", SetLastError = true), SuppressUnmanagedCodeSecurity]
        public static extern bool CloseHandle(IntPtr hObject);

        /// <summary>Reads volume identity, used to key a journal cursor independently of drive letter</summary>
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode), SuppressUnmanagedCodeSecurity]
        public static extern bool GetVolumeInformationW(
            string lpRootPathName,
            [Out] char[]? lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            [Out] char[]? lpFileSystemNameBuffer,
            uint nFileSystemNameSize
        );

        // Journal-specific error codes returned through Marshal.GetLastWin32Error.
        public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
        public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
        public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;
        public const int ERROR_INVALID_FUNCTION = 1;
        public const int ERROR_HANDLE_EOF = 38;

        // Access flags for file operations
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

        // GetFinalPathNameByHandle flags
        public const uint FILE_NAME_NORMALIZED = 0x0;
        public const uint FILE_NAME_OPENED = 0x8;
        public const uint VOLUME_NAME_DOS = 0x0;
        public const uint VOLUME_NAME_GUID = 0x1;
        public const uint VOLUME_NAME_NONE = 0x4;
        public const uint VOLUME_NAME_NT = 0x2;
    }
}
