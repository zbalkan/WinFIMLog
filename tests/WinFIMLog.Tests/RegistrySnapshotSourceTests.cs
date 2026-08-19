using System;
using System.Linq;
using System.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RegistrySnapshotSourceTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void Capture_prunes_excluded_registry_subtrees()
    {
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

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void Capture_records_typed_values_and_key_acl_evidence()
    {
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
            Assert.StartsWith(@"HKEY_USERS\S-1-", expandedRoot);
            var number = members.Single(x => x.Path == expandedRoot + @"\Number");
            Assert.AreEqual(RegistryValueKind.DWord.ToString(), number.RegistryValueKind);
            Assert.IsNotNull(number.RegistryValueData);
            Assert.AreEqual(SnapshotNodeType.RegistryKey, members.Single(x => x.Path == expandedRoot).NodeType);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    [TestMethod]
    public void Key_and_value_at_the_same_path_have_distinct_identities()
    {
        var path = @"HKEY_LOCAL_MACHINE\Software\Vendor\Name";

        Assert.AreNotEqual(
            RegistrySnapshotSource.Identity(path, SnapshotNodeType.RegistryKey),
            RegistrySnapshotSource.Identity(path, SnapshotNodeType.RegistryValue));
    }

    [TestMethod]
    public void ResolveRoots_removes_duplicate_and_descendant_roots()
    {
        var roots = RegistrySnapshotSource.ResolveRoots([
            @"HKEY_LOCAL_MACHINE\Software\Vendor\Product",
            @"HKEY_LOCAL_MACHINE\Software",
            @"hkey_local_machine\software\",
            @"HKEY_USERS\S-1-5-18\Software"
        ]);

        Assert.AreSequenceEqual([
            @"HKEY_LOCAL_MACHINE\Software",
            @"HKEY_USERS\S-1-5-18\Software"
        ], roots);
    }

    [TestMethod]
    public void ResolveRoots_preserves_siblings_with_misleading_textual_prefixes()
    {
        var roots = RegistrySnapshotSource.ResolveRoots([
            @"HKEY_LOCAL_MACHINE\SOFT",
            @"HKEY_LOCAL_MACHINE\SOFTWARE",
            @"HKEY_LOCAL_MACHINE\SOFT\Child",
            @"HKEY_LOCAL_MACHINE\SYSTEM"
        ]);

        Assert.AreSequenceEqual([
            @"HKEY_LOCAL_MACHINE\SOFT",
            @"HKEY_LOCAL_MACHINE\SOFTWARE",
            @"HKEY_LOCAL_MACHINE\SYSTEM"
        ], roots);
    }

    [TestMethod]
    public void Registry_security_exceptions_are_classified_as_access_denied()
    {
        Assert.IsTrue(RegistrySnapshotSource.IsAccessDenied(new UnauthorizedAccessException()));
        Assert.IsTrue(RegistrySnapshotSource.IsAccessDenied(new SecurityException()));
        Assert.IsFalse(RegistrySnapshotSource.IsAccessDenied(new InvalidOperationException()));
    }
}
