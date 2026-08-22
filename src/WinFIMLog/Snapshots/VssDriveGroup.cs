using System;
using System.Collections.Generic;
using System.IO;

namespace WinFIMLog.Snapshots
{
    /// <summary>Configured filesystem roots that share one local Windows source volume.</summary>
    public sealed class VssDriveGroup
    {
        public VssDriveGroup(string sourceVolumeRoot, IReadOnlyList<string> monitoredRoots)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceVolumeRoot);
            ArgumentNullException.ThrowIfNull(monitoredRoots);
            if (monitoredRoots.Count == 0) throw new ArgumentException("At least one monitored root is required.", nameof(monitoredRoots));
            SourceVolumeRoot = GetSupportedDriveVolumeRoot(sourceVolumeRoot);
            MonitoredRoots = monitoredRoots;
        }
        public IReadOnlyList<string> MonitoredRoots { get; }
        public string SourceVolumeRoot { get; }
        public static IReadOnlyList<VssDriveGroup> GroupByDrive(IReadOnlyList<string> monitoredRoots)
        {
            ArgumentNullException.ThrowIfNull(monitoredRoots);
            var byDrive = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var root in monitoredRoots)
            {
                var volume = GetSupportedDriveVolumeRoot(root);
                if (!byDrive.TryGetValue(volume, out var roots)) { roots = []; byDrive.Add(volume, roots); order.Add(volume); }
                roots.Add(root);
            }
            if (order.Count == 0) throw new ArgumentException("At least one monitored local drive root is required.", nameof(monitoredRoots));
            var groups = new List<VssDriveGroup>(order.Count);
            foreach (var volume in order) groups.Add(new VssDriveGroup(volume, byDrive[volume]));
            return groups;
        }
        internal static string GetSupportedDriveVolumeRoot(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (path.Length < 3 || !IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                throw new ArgumentException($"VSS filesystem snapshots support only absolute local drive-letter paths; '{path}' is not supported.", nameof(path));
            return string.Concat(char.ToUpperInvariant(path[0]), @":\");
        }
        private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    internal sealed class SnapshotPathMap
    {
        private readonly Mapping[] mappings;
        public SnapshotPathMap(IReadOnlyDictionary<string, string> snapshotRoots)
        {
            ArgumentNullException.ThrowIfNull(snapshotRoots);
            if (snapshotRoots.Count == 0) throw new ArgumentException("At least one snapshot mapping is required.", nameof(snapshotRoots));
            mappings = new Mapping[snapshotRoots.Count]; var index = 0;
            foreach (var pair in snapshotRoots)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pair.Value);
                mappings[index++] = new(VssDriveGroup.GetSupportedDriveVolumeRoot(pair.Key), Path.TrimEndingDirectorySeparator(pair.Value));
            }
        }
        internal string GetVolumeDevicePath(string sourceVolumeRoot)
        {
            var root = VssDriveGroup.GetSupportedDriveVolumeRoot(sourceVolumeRoot);
            foreach (var mapping in mappings) if (string.Equals(mapping.LiveVolumeRoot, root, StringComparison.OrdinalIgnoreCase)) return mapping.SnapshotRoot;
            throw new ArgumentException($"No VSS snapshot exists for '{root}'.", nameof(sourceVolumeRoot));
        }
        internal string ToCapturePath(string livePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(livePath);
            var root = VssDriveGroup.GetSupportedDriveVolumeRoot(livePath);
            return string.Concat(GetVolumeDevicePath(root), livePath.AsSpan(2));
        }
        internal string ToEvidencePath(string capturePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(capturePath);
            foreach (var mapping in mappings)
            {
                if (!capturePath.StartsWith(mapping.SnapshotRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (capturePath.Length != mapping.SnapshotRoot.Length && !IsSeparator(capturePath[mapping.SnapshotRoot.Length])) continue;
                var suffix = capturePath.AsSpan(mapping.SnapshotRoot.Length);
                return suffix.Length == 0 ? mapping.LiveVolumeRoot : string.Concat(mapping.LiveVolumeRoot.AsSpan(0, 2), suffix);
            }
            throw new ArgumentException($"The capture path '{capturePath}' is not within a VSS snapshot root.", nameof(capturePath));
        }
        private static bool IsSeparator(char value) => value == '\\' || value == '/';
        private readonly record struct Mapping(string LiveVolumeRoot, string SnapshotRoot);
    }
}
