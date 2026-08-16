using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;

namespace WinFIMLog.Snapshots
{
    internal static class SourceIdentityProvider
    {
        public static string FileSystem(IEnumerable<string> paths) => string.Join(";",
            paths.Select(VolumeIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

        public static string RegistryResolved(IEnumerable<string> resolvedKeys) => string.Join(";",
            resolvedKeys.Select(key => key.TrimEnd('\\').ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

        private static string VolumeIdentity(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
            if (!OperatingSystem.IsWindows()) return root;
            var volumeName = new StringBuilder(261);
            var fileSystemName = new StringBuilder(261);
            if (!GetVolumeInformation(root, volumeName, volumeName.Capacity, out var serial, out _, out _,
                    fileSystemName, fileSystemName.Capacity))
                throw new IOException($"Cannot identify volume '{root}'.", new Win32Exception(Marshal.GetLastWin32Error()));
            return $"{root}|{serial:X8}|{fileSystemName}";
        }

        [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(string rootPathName, StringBuilder volumeNameBuffer,
            int volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength,
            out uint fileSystemFlags, StringBuilder fileSystemNameBuffer, int fileSystemNameSize);
    }
}
