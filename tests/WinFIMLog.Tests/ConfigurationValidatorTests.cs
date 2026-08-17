using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class ConfigurationValidatorTests
{
    [TestMethod]
    [DataRow(@"C:\Program Files (x86)\App[1]")]
    [DataRow(@"C:\Users\*\Downloads")]
    [DataRow(@"C:\Data\a+b (copy)")]
    public void Accepts_absolute_paths_with_regex_special_characters(string value) =>
        ConfigurationValidator.ValidatePath(value);

    [TestMethod]
    [DataRow(@"HKEY_LOCAL_MACHINE\Software\A+B (test)")]
    [DataRow(@"HKEY_CURRENT_USER\Software\Example[1]")]
    public void Accepts_keys_with_regex_special_characters(string value) => ConfigurationValidator.ValidateKey(value);

    [TestMethod]
    [DataRow(@"HKLM\Software\Example")]
    [DataRow(@"HKEY_USERS\*\Software")]
    [DataRow("")]
    public void Rejects_malformed_keys_and_names_value(string value)
    {
        var exception = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.ValidateKey(value));
        Assert.Contains(value, exception.Message);
    }

    [TestMethod]
    [DataRow("relative\\path")]
    [DataRow(@"C:\Us*ers\Downloads")]
    [DataRow(@"C:\Users\*\*\Downloads")]
    [DataRow(@"C:\Users\?\Downloads")]
    public void Rejects_malformed_paths_and_names_value(string value)
    {
        var exception = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.ValidatePath(value));
        Assert.Contains(value, exception.Message);
    }
}
