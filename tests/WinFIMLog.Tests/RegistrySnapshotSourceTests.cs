using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RegistrySnapshotSourceTests
{
    [TestMethod]
    public void Capture_records_typed_values_and_key_acl_evidence()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows Registry required."); return; }
        var path = $@"Software\WinFIMLogTests\{Guid.NewGuid():N}";
        using (var key = Registry.CurrentUser.CreateSubKey(path))
        {
            key.SetValue("Number", 42, RegistryValueKind.DWord);
            key.SetValue("Words", new[] { "one", "two" }, RegistryValueKind.MultiString);
        }
        try
        {
            var root = @"HKEY_CURRENT_USER\" + path;
            var members = new RegistrySnapshotSource().Capture([root]);
            var expandedRoot = members.Single(x => x.NodeType == SnapshotNodeType.RegistryKey &&
                x.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase)).Path;
            StringAssert.StartsWith(expandedRoot, @"HKEY_USERS\S-1-");
            var number = members.Single(x => x.Path == expandedRoot + @"\Number");
            Assert.AreEqual(RegistryValueKind.DWord.ToString(), number.RegistryValueKind);
            Assert.IsNotNull(number.RegistryValueData);
            Assert.AreEqual(SnapshotNodeType.RegistryKey, members.Single(x => x.Path == expandedRoot).NodeType);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [TestMethod]
    public void Capture_prunes_excluded_registry_subtrees()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows Registry required."); return; }
        var path = $@"Software\WinFIMLogTests\{Guid.NewGuid():N}";
        using (var root = Registry.CurrentUser.CreateSubKey(path))
        {
            root.SetValue("Included", 1);
            using var excluded = root.CreateSubKey("Excluded");
            excluded.SetValue("Hidden", 2);
        }
        try
        {
            var configuredRoot = @"HKEY_CURRENT_USER\" + path;
            var members = new RegistrySnapshotSource(candidate =>
                !candidate.Contains(@"\Excluded", StringComparison.OrdinalIgnoreCase)).Capture([configuredRoot]);

            Assert.Contains(member => member.Path.EndsWith(path + @"\Included", StringComparison.OrdinalIgnoreCase), members);
            Assert.DoesNotContain(member => member.Path.Contains(@"\Excluded", StringComparison.OrdinalIgnoreCase), members);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
}
