using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class SourceIdentityProviderTests
{
    [TestMethod]
    public void Registry_identity_is_canonical_and_distinguishes_hives()
    {
        var identity = SourceIdentityProvider.Registry([
            @"HKEY_LOCAL_MACHINE\Software\A",
            @"hkey_local_machine\Software\B",
            @"HKEY_USERS\S-1-5-18\Software\A"]);

        StringAssert.Contains(identity, "HKEY_LOCAL_MACHINE");
        StringAssert.Contains(identity, "HKEY_USERS");
        Assert.AreEqual(2, identity.Split(';').Length);
    }
}
