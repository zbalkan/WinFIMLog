using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemSnapshotSourceTests
{
    [TestMethod]
    public void Capture_RecordsDirectoriesFilesAndHashEvidenceSeparately()
    {
        var root = Directory.CreateTempSubdirectory("winfimlog-snapshot-");
        try
        {
            var file = Path.Combine(root.FullName, "evidence.txt");
            File.WriteAllText(file, "persistent evidence");
            var members = new FileSystemSnapshotSource(1).Capture(new[] { root.FullName });

            Assert.AreEqual(2, members.Count);
            Assert.AreEqual(SnapshotNodeType.Directory, members.Single(x => x.Path == root.FullName).NodeType);
            var evidence = members.Single(x => x.Path == file);
            Assert.AreEqual(SnapshotNodeType.File, evidence.NodeType);
            Assert.AreEqual(HashEvidenceState.Hashed, evidence.HashState);
            Assert.IsFalse(string.IsNullOrWhiteSpace(evidence.ContentHash));
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void Capture_RecordsSizeCapAsEvidenceRatherThanEmptyHash()
    {
        var root = Directory.CreateTempSubdirectory("winfimlog-snapshot-");
        try
        {
            var file = Path.Combine(root.FullName, "large.bin");
            File.WriteAllBytes(file, new byte[] { 1 });
            var evidence = new FileSystemSnapshotSource(0).Capture(new[] { root.FullName }).Single(x => x.Path == file);
            Assert.AreEqual(HashEvidenceState.SkippedBySizeCap, evidence.HashState);
            Assert.IsNull(evidence.ContentHash);
        }
        finally { root.Delete(true); }
    }
}
