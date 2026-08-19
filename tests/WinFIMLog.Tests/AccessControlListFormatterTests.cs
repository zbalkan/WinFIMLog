using System;
using System.Security.AccessControl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.IO.Security;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class AccessControlListFormatterTests
{
    [TestMethod]
    public void Typed_aces_are_rendered_as_human_readable_key_value_pairs()
    {
        using var accessControlList = new AccessControlList(initialCapacity: 1);
        accessControlList.Add(new AccessControlEntry(
            Identity: null!,
            Rights: 0x001F01FF,
            Type: AccessControlType.Allow,
            IsInherited: true,
            InheritanceFlags: InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
            PropagationFlags: PropagationFlags.None));

        var rendered = AccessControlListFormatter.Format(accessControlList);

        Assert.AreEqual(1, accessControlList.Count);
        Assert.Contains("Owner: None; PrimaryGroup: None; AceCount: 1", rendered);
        Assert.Contains("ACE: [Identity: None; Rights: 0x001F01FF; Type: Allow; Inherited: true; Inheritance: ObjectInherit|ContainerInherit; Propagation: None]", rendered);
        Assert.IsFalse(rendered.Contains('{'));
    }

    [TestMethod]
    public void Equivalent_typed_acls_reuse_the_same_rendered_string()
    {
        using var first = CreateAcl(0x001F01FE);
        using var second = CreateAcl(0x001F01FE);

        var initial = AccessControlListFormatter.Format(first);
        var cached = AccessControlListFormatter.Format(second);

        Assert.AreSame(initial, cached);
    }

    [TestMethod]
    public void Warm_typed_acl_cache_hit_does_not_allocate_in_the_formatter()
    {
        using var accessControlList = CreateAcl(0x001F01FD);
        var expected = AccessControlListFormatter.Format(accessControlList);
        _ = AccessControlListFormatter.Format(accessControlList);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = AccessControlListFormatter.Format(accessControlList);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreSame(expected, actual);
        Assert.AreEqual(0L, allocated);
    }

    [TestMethod]
    public void Disposed_acl_does_not_expose_its_rented_ace_buffer()
    {
        var accessControlList = new AccessControlList();
        accessControlList.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = accessControlList.Entries);
    }

    private static AccessControlList CreateAcl(uint rights)
    {
        var accessControlList = new AccessControlList(initialCapacity: 1);
        accessControlList.Add(new AccessControlEntry(
            Identity: null!,
            Rights: rights,
            Type: AccessControlType.Allow,
            IsInherited: false,
            InheritanceFlags: InheritanceFlags.None,
            PropagationFlags: PropagationFlags.None));
        return accessControlList;
    }
}
