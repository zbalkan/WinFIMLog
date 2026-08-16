using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class ConfigurationPrecedenceTests
{
    [TestMethod]
    public void Policy_overrides_preference_and_legacy()
    {
        Assert.AreEqual("policy", ConfigurationPrecedence.Resolve("policy", "preference", "legacy"));
    }

    [TestMethod]
    public void Policy_removal_reveals_preference()
    {
        Assert.AreEqual("preference", ConfigurationPrecedence.Resolve(null, "preference", "legacy"));
    }

    [TestMethod]
    public void Legacy_is_only_the_last_migration_fallback()
    {
        Assert.AreEqual("legacy", ConfigurationPrecedence.Resolve(null, null, "legacy"));
        Assert.IsNull(ConfigurationPrecedence.Resolve(null, null, null));
    }
}
