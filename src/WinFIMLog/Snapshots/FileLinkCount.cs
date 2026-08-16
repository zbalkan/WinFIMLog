using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinFIMLog.Snapshots
{
    internal static class FileLinkCount
    {
        public static int? TryGet(string path)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                using SafeFileHandle handle = System.IO.File.OpenHandle(path);
                return GetFileInformationByHandle(handle, out var information)
                    ? checked((int)information.NumberOfLinks) : null;
            }
            catch { return null; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file,
            out ByHandleFileInformation information);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
    }
}
