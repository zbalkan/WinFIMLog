using WinFIMLog.Configuration;
using Xunit;

namespace WinFIMLog.Tests;

public sealed class RegistryScopeMatcherTests
{
    [Fact]
    public void Current_user_configuration_matches_all_loaded_sid_hives()
    {
        var matcher = new RegistryScopeMatcher(
            [@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run"], []);
        Assert.True(matcher.IsMatch(@"HKEY_USERS\S-1-5-21-1-2-3-1001\Software\Microsoft\Windows\CurrentVersion\Run\Example"));
    }

    [Fact]
    public void Explicit_users_configuration_retains_literal_semantics()
    {
        var matcher = new RegistryScopeMatcher([@"HKEY_USERS\S-1-5-18\Software\Example"], []);
        Assert.True(matcher.IsMatch(@"HKEY_USERS\S-1-5-18\Software\Example\Value"));
        Assert.False(matcher.IsMatch(@"HKEY_USERS\S-1-5-19\Software\Example\Value"));
    }

    [Fact]
    public void Exclusion_takes_precedence_after_current_user_normalisation()
    {
        var matcher = new RegistryScopeMatcher([@"HKEY_CURRENT_USER\Software"],
            [@"HKEY_CURRENT_USER\Software\Excluded"]);
        Assert.False(matcher.IsMatch(@"HKEY_USERS\S-1-5-21-9-1001\Software\Excluded\Value"));
    }
}
