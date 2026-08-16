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

        Assert.Contains("HKEY_LOCAL_MACHINE", identity);
        Assert.Contains("HKEY_USERS", identity);
        Assert.HasCount(2, identity.Split(';'));
    }
}
