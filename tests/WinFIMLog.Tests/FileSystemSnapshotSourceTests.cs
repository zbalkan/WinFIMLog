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

    [TestMethod]
    public void Fingerprint_distinguishes_node_attribute_evidence()
    {
        var ordinary = new BaselineMember { Identity = "a", IsSparse = false };
        var sparse = new BaselineMember { Identity = "a", IsSparse = true };
        Assert.AreNotEqual(ordinary.Fingerprint, sparse.Fingerprint);
    }

    [TestMethod]
    public void Capture_records_reparse_point_without_traversing_target()
    {
        var root = Directory.CreateTempSubdirectory("winfimlog-root-");
        var target = Directory.CreateTempSubdirectory("winfimlog-target-");
        try
        {
            File.WriteAllText(Path.Combine(target.FullName, "outside.txt"), "evidence");
            var link = Path.Combine(root.FullName, "link");
            try { Directory.CreateSymbolicLink(link, target.FullName); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException)
            { Assert.Inconclusive($"Symbolic links unavailable: {exception.Message}"); return; }

            var members = new FileSystemSnapshotSource(1).Capture([root.FullName]);
            Assert.AreEqual(SnapshotNodeType.ReparsePoint, members.Single(x => x.Path == link).NodeType);
            Assert.IsFalse(members.Any(x => x.Path.EndsWith("outside.txt", StringComparison.Ordinal)));
        }
        finally { root.Delete(true); target.Delete(true); }
    }

    [TestMethod]
    public void Locked_file_has_an_explicit_hash_state_on_windows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows file sharing semantics required."); return; }
        var root = Directory.CreateTempSubdirectory("winfimlog-lock-");
        var file = Path.Combine(root.FullName, "locked.txt");
        File.WriteAllText(file, "locked");
        try
        {
            using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var member = new FileSystemSnapshotSource(1).Capture([file]).Single();
            Assert.AreEqual(HashEvidenceState.Locked, member.HashState);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void Named_ads_is_listed_but_unnamed_stream_remains_the_content_hash()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("NTFS alternate data streams required."); return; }
        var root = Directory.CreateTempSubdirectory("winfimlog-ads-");
        var file = Path.Combine(root.FullName, "streams.txt");
        try
        {
            File.WriteAllText(file, "unnamed");
            File.WriteAllText(file + ":evidence", "named");
            var member = new FileSystemSnapshotSource(1).Capture([file]).Single();
            Assert.IsTrue(member.StreamNames.Any(name => name.Contains(":evidence:", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("unnamed"))), member.ContentHash);
        }
        finally { root.Delete(true); }
    }
}
