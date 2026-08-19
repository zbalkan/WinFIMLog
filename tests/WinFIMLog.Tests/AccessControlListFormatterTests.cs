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
    public void Disposed_acl_does_not_expose_its_rented_ace_buffer()
    {
        var accessControlList = new AccessControlList();
        accessControlList.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = accessControlList.Entries);
    }
}
