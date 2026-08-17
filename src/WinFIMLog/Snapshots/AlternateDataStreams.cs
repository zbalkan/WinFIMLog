using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WinFIMLog.Snapshots
{
    internal static partial class AlternateDataStreams
    {
        internal static string[] Enumerate(string path)
        {
            var names = new List<string>();
            var handle = FindFirstStream(path, 0, out var data, 0);
            if (handle == new IntPtr(-1))
            {
                return [];
            }

            try
            {
                do
                {
                    // The default ::$DATA is deliberately excluded; only its content is hashed.
                    if (!string.Equals(data.Name, "::$DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(data.Name);
                    }
                } while (FindNextStream(handle, out data));
            }
            finally { FindClose(handle); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool FindClose(IntPtr handle);

        [DllImport("kernel32.dll", EntryPoint = "FindFirstStreamW", SetLastError = true, CharSet = CharSet.Unicode)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability",
            "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
            Justification = "StreamData does not support compile time P/Invoke")]
        private extern static IntPtr FindFirstStream(string fileName, int infoLevel, out StreamData data, int flags);

        [DllImport("kernel32.dll", EntryPoint = "FindNextStreamW", SetLastError = true)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability",
            "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
            Justification = "StreamData does not support compile time P/Invoke")]
        [return: MarshalAs(UnmanagedType.Bool)] private extern static bool FindNextStream(IntPtr handle, out StreamData data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StreamData
        { public long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name; }
    }
}
