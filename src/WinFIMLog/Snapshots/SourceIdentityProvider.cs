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

        public static string Registry(IEnumerable<string> keys)
        {
            var loadedUsers = OperatingSystem.IsWindows()
                ? string.Join(",", Microsoft.Win32.Registry.Users.GetSubKeyNames()
                    .Where(name => name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase))
                : "non-windows";
            return string.Join(";", keys.Select(key => key.Split('\\', 2)[0].ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                .Select(hive => hive == "HKEY_CURRENT_USER" ? $"{hive}:{loadedUsers}" : hive));
        }

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
