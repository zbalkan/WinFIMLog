using WinFIMLog.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class ConfigurationValidatorTests
{
    [DataTestMethod]
    [DataRow(@"C:\Program Files (x86)\App[1]")]
    [DataRow(@"C:\Users\*\Downloads")]
    [DataRow(@"C:\Data\a+b (copy)")]
    public void Accepts_absolute_paths_with_regex_special_characters(string value) =>
        ConfigurationValidator.ValidatePath(value);

    [DataTestMethod]
    [DataRow("relative\\path")]
    [DataRow(@"C:\Us*ers\Downloads")]
    [DataRow(@"C:\Users\*\*\Downloads")]
    [DataRow(@"C:\Users\?\Downloads")]
    public void Rejects_malformed_paths_and_names_value(string value)
    {
        var exception = Assert.ThrowsException<ConfigurationValidationException>(() => ConfigurationValidator.ValidatePath(value));
        StringAssert.Contains(exception.Message, value);
    }

    [DataTestMethod]
    [DataRow(@"HKEY_LOCAL_MACHINE\Software\A+B (test)")]
    [DataRow(@"HKEY_CURRENT_USER\Software\Example[1]")]
    public void Accepts_keys_with_regex_special_characters(string value) => ConfigurationValidator.ValidateKey(value);

    [DataTestMethod]
    [DataRow(@"HKLM\Software\Example")]
    [DataRow(@"HKEY_USERS\*\Software")]
    [DataRow("")]
    public void Rejects_malformed_keys_and_names_value(string value)
    {
        var exception = Assert.ThrowsException<ConfigurationValidationException>(() => ConfigurationValidator.ValidateKey(value));
        StringAssert.Contains(exception.Message, value);
    }
}
