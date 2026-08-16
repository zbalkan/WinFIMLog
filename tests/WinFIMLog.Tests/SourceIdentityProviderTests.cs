using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class SourceIdentityProviderTests
{
    [TestMethod]
    public void Resolved_registry_identity_is_canonical_and_distinguishes_roots()
    {
        var identity = SourceIdentityProvider.RegistryResolved([
            @"HKEY_LOCAL_MACHINE\Software\A",
            @"hkey_local_machine\Software\A",
            @"HKEY_USERS\S-1-5-18\Software\A"]);

        Assert.Contains(@"HKEY_LOCAL_MACHINE\SOFTWARE\A", identity);
        Assert.Contains(@"HKEY_USERS\S-1-5-18\SOFTWARE\A", identity);
        Assert.HasCount(2, identity.Split(';'));
    }

    [TestMethod]
    public void Resolved_registry_identity_versions_the_concrete_loaded_hive_scope()
    {
        var first = SourceIdentityProvider.RegistryResolved([
            @"HKEY_USERS\S-1-5-21-1\Software\Run"]);
        var second = SourceIdentityProvider.RegistryResolved([
            @"HKEY_USERS\S-1-5-21-2\Software\Run"]);

        Assert.AreNotEqual(first, second);
    }
}
