using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
            var members = new FileSystemSnapshotSource(1).Capture([root.FullName]);

            Assert.HasCount(2, members);
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
            File.WriteAllBytes(file, [1]);
            var evidence = new FileSystemSnapshotSource(0).Capture([root.FullName]).Single(x => x.Path == file);
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
    public void Capture_applies_exclusions_and_prunes_excluded_directories()
    {
        var root = Directory.CreateTempSubdirectory("winfimlog-scope-");
        try
        {
            var included = Path.Combine(root.FullName, "included.txt");
            var excludedExtension = Path.Combine(root.FullName, "volatile.log");
            var excludedDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "excluded"));
            var excludedChild = Path.Combine(excludedDirectory.FullName, "must-not-be-visited.txt");
            File.WriteAllText(included, "included");
            File.WriteAllText(excludedExtension, "excluded");
            File.WriteAllText(excludedChild, "excluded subtree");
            var evaluated = new List<string>();

            bool IsIncluded(string path)
            {
                evaluated.Add(path);
                return !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith(excludedDirectory.FullName, StringComparison.OrdinalIgnoreCase);
            }

            var members = new FileSystemSnapshotSource(1, IsIncluded).Capture([root.FullName]);

            Assert.Contains(member => member.Path == included, members);
            Assert.DoesNotContain(member => member.Path == excludedExtension, members);
            Assert.DoesNotContain(member => member.Path == excludedDirectory.FullName, members);
            Assert.DoesNotContain(path => path == excludedChild, evaluated,
                "An excluded directory must prune traversal before its children are evaluated.");
        }
        finally { root.Delete(true); }
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
            Assert.DoesNotContain(x => x.Path.EndsWith("outside.txt", StringComparison.Ordinal), members);
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
            Assert.Contains(name => name.Contains(":evidence:", StringComparison.OrdinalIgnoreCase), member.StreamNames);
            Assert.AreEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("unnamed"))), member.ContentHash);
        }
        finally { root.Delete(true); }
    }
}
