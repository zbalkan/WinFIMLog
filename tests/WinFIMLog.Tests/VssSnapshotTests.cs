using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class VssSnapshotTests
{
    [TestMethod]
    public void Drive_groups_are_case_insensitive_and_preserve_first_seen_order()
    {
        var groups = VssDriveGroup.GroupByDrive([@"d:\one", @"C:\two", @"D:\three"]);
        Assert.HasCount(2, groups);
        Assert.AreEqual(@"D:\", groups[0].SourceVolumeRoot);
        Assert.AreSequenceEqual(new[] { @"d:\one", @"D:\three" }, new List<string>(groups[0].MonitoredRoots));
        Assert.AreEqual(@"C:\", groups[1].SourceVolumeRoot);
    }

    [TestMethod]
    public void Drive_groups_reject_unc_and_relative_paths()
    {
        Assert.Throws<ArgumentException>(() => VssDriveGroup.GroupByDrive([@"\\server\share"]));
        Assert.Throws<ArgumentException>(() => VssDriveGroup.GroupByDrive([@"relative\path"]));
    }

    [TestMethod]
    public void Snapshot_map_round_trips_paths_and_rejects_similar_prefixes()
    {
        const string device = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1";
        var map = new SnapshotPathMap(new Dictionary<string, string> { [@"C:\"] = device });
        Assert.AreEqual(device + @"\Evidence\a.txt", map.ToCapturePath(@"C:\Evidence\a.txt"));
        Assert.AreEqual(@"C:\Evidence\a.txt", map.ToEvidencePath(device + @"\Evidence\a.txt"));
        Assert.AreEqual(@"C:\", map.ToEvidencePath(device));
        Assert.Throws<ArgumentException>(() => map.ToEvidencePath(device + @"0\Windows"));
    }
}
