using WinFIMLog.Configuration;
using Xunit;

namespace WinFIMLog.Tests;

public sealed class ConfigurationValidatorTests
{
    [Theory]
    [InlineData(@"C:\Program Files (x86)\App[1]")]
    [InlineData(@"C:\Users\*\Downloads")]
    [InlineData(@"C:\Data\a+b (copy)")]
    public void Accepts_absolute_paths_with_regex_special_characters(string value) =>
        ConfigurationValidator.ValidatePath(value);

    [Theory]
    [InlineData("relative\\path")]
    [InlineData(@"C:\Us*ers\Downloads")]
    [InlineData(@"C:\Users\*\*\Downloads")]
    [InlineData(@"C:\Users\?\Downloads")]
    public void Rejects_malformed_paths_and_names_value(string value)
    {
        var exception = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.ValidatePath(value));
        Assert.Contains(value, exception.Message);
    }

    [Theory]
    [InlineData(@"HKEY_LOCAL_MACHINE\Software\A+B (test)")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Example[1]")]
    public void Accepts_keys_with_regex_special_characters(string value) => ConfigurationValidator.ValidateKey(value);

    [Theory]
    [InlineData(@"HKLM\Software\Example")]
    [InlineData(@"HKEY_USERS\*\Software")]
    [InlineData("")]
    public void Rejects_malformed_keys_and_names_value(string value)
    {
        var exception = Assert.Throws<ConfigurationValidationException>(() => ConfigurationValidator.ValidateKey(value));
        Assert.Contains(value, exception.Message);
    }
}
