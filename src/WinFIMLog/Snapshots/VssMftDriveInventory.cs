using System;
using System.Collections.Generic;
using System.IO.Filesystem.Ntfs;

namespace WinFIMLog.Snapshots
{
    public interface IVssDriveInventory
    {
        IReadOnlyList<BaselineMember> Capture(VssDriveGroup driveGroup, IFileSystemSnapshot snapshot, EffectiveSettings configuration);
    }
    public sealed class VssMftDriveInventory : IVssDriveInventory
    {
        public IReadOnlyList<BaselineMember> Capture(VssDriveGroup driveGroup, IFileSystemSnapshot snapshot, EffectiveSettings configuration)
        {
            ArgumentNullException.ThrowIfNull(driveGroup); ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(configuration);
            using var reader = new NtfsReader(
                new System.IO.DriveInfo(driveGroup.SourceVolumeRoot),
                RetrieveMode.StandardInformations);
            var source = new FileSystemSnapshotSource(configuration.HashLimitMB, configuration.IsMonitoredPath, snapshot.ToEvidencePath);
            return source.CaptureMftPaths(FilteredCapturePaths(reader.GetNodes(driveGroup.SourceVolumeRoot), snapshot, configuration));
        }
        private static IEnumerable<string> FilteredCapturePaths(IReadOnlyList<INode> nodes, IFileSystemSnapshot snapshot, EffectiveSettings configuration)
        { foreach (var node in nodes)
            {
                if (configuration.IsMonitoredPath(node.FullName))
                {
                    yield return snapshot.ToCapturePath(node.FullName);
                }
            }
        }
    }
}
