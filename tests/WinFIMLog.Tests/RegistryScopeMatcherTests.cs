using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RegistryScopeMatcherTests
{
    [TestMethod]
    public void Current_user_configuration_matches_all_loaded_sid_hives()
    {
        var matcher = new RegistryScopeMatcher(
            [@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run"], []);
        Assert.IsTrue(matcher.IsMatch(@"HKEY_USERS\S-1-5-21-1-2-3-1001\Software\Microsoft\Windows\CurrentVersion\Run\Example"));
    }

    [TestMethod]
    public void Exclusion_takes_precedence_after_current_user_normalisation()
    {
        var matcher = new RegistryScopeMatcher([@"HKEY_CURRENT_USER\Software"],
            [@"HKEY_CURRENT_USER\Software\Excluded"]);
        Assert.IsFalse(matcher.IsMatch(@"HKEY_USERS\S-1-5-21-9-1001\Software\Excluded\Value"));
    }

    [TestMethod]
    public void Explicit_users_configuration_retains_literal_semantics()
    {
        var matcher = new RegistryScopeMatcher([@"HKEY_USERS\S-1-5-18\Software\Example"], []);
        Assert.IsTrue(matcher.IsMatch(@"HKEY_USERS\S-1-5-18\Software\Example\Value"));
        Assert.IsFalse(matcher.IsMatch(@"HKEY_USERS\S-1-5-19\Software\Example\Value"));
    }
}
