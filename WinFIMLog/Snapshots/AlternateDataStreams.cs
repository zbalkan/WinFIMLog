using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinFIMLog.Snapshots
{
    internal static class AlternateDataStreams
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StreamData { public long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name; }

        [DllImport("kernel32.dll", EntryPoint = "FindFirstStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstStream(string fileName, int infoLevel, out StreamData data, int flags);
        [DllImport("kernel32.dll", EntryPoint = "FindNextStreamW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextStream(IntPtr handle, out StreamData data);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);

        internal static string[] Enumerate(string path)
        {
            var names = new List<string>();
            var handle = FindFirstStream(path, 0, out var data, 0);
            if (handle == new IntPtr(-1)) return Array.Empty<string>();
            try
            {
                do
                {
                    // The default ::$DATA is deliberately excluded; only its content is hashed.
                    if (!string.Equals(data.Name, "::$DATA", StringComparison.OrdinalIgnoreCase)) names.Add(data.Name);
                } while (FindNextStream(handle, out data));
            }
            finally { FindClose(handle); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }
    }
}
