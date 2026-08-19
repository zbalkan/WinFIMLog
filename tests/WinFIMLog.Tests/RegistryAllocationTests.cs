using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RegistryAllocationTests
{
    [TestMethod]
    public void Current_user_configuration_matches_sid_hive_without_prefix_or_path_temporaries()
    {
        Assert.IsTrue(RegistryScopeMatcher.Matches(
            @"HKEY_CURRENT_USER\Software\Vendor\Product\",
            @"HKEY_USERS\S-1-5-21-1-2-3-1001\Software\Vendor\Product\Value"));
    }

    [TestMethod]
    public void Current_user_configuration_preserves_segment_boundaries_after_sid_normalisation()
    {
        Assert.IsFalse(RegistryScopeMatcher.Matches(
            @"HKEY_CURRENT_USER\Software\Prod",
            @"HKEY_USERS\S-1-5-21-1-2-3-1001\Software\Product\Value"));
        Assert.IsFalse(RegistryScopeMatcher.Matches(
            @"HKEY_CURRENT_USER\Software",
            @"HKEY_USERS\NotASid\Software\Value"));
    }

    [TestMethod]
    public void Registry_key_construction_returns_one_normalized_final_string()
    {
        Assert.AreEqual(@"HKEY_LOCAL_MACHINE\Software\Vendor\Value",
            RegistryMonitorJob.CombineFullKeyName(@"\registry\machine\Software", "Vendor", "Value"));
        Assert.AreEqual(@"HKEY_USERS\S-1-5-18\Software",
            RegistryMonitorJob.CombineFullKeyName(@"\REGISTRY\USER\S-1-5-18", "Software", null));
        Assert.AreEqual(string.Empty, RegistryMonitorJob.CombineFullKeyName(null, " ", null));
    }

    [TestMethod]
    public void Registry_key_parser_preserves_literal_value_name_and_root_behavior()
    {
        Assert.AreEqual(@"Software\Vendor", RegistryChange.StripFullName(
            @"HKEY_LOCAL_MACHINE\Software\Vendor\a.b[1]", "a.b[1]"));
        Assert.AreEqual(@"Software\Vendor\VALUE", RegistryChange.StripFullName(
            @"HKEY_LOCAL_MACHINE\Software\Vendor\VALUE", "Value"));
        Assert.AreEqual("HKEY_LOCAL_MACHINE", RegistryChange.StripFullName(
            @"HKEY_LOCAL_MACHINE\Value", "Value"));
        Assert.AreEqual("NoHiveSeparator", RegistryChange.StripFullName("NoHiveSeparator", "Value"));
    }

    [TestMethod]
    public void Registry_binary_value_serialization_keeps_the_existing_lowercase_spaced_format()
    {
        Assert.AreEqual("00 0f a5 ff", RegistryChange.FormatBinaryValue([0x00, 0x0f, 0xa5, 0xff]));
        Assert.AreEqual(string.Empty, RegistryChange.FormatBinaryValue([]));
    }

    [TestMethod]
    public void Path_scope_matching_accepts_both_separator_boundaries_without_building_prefix_strings()
    {
        Assert.IsTrue(PathScopeMatcher.IsWithin(@"C:\Watch", @"c:\watch/child\evidence.txt"));
        Assert.IsTrue(PathScopeMatcher.IsWithin(@"C:\Watch\", @"C:\Watch\child"));
        Assert.IsFalse(PathScopeMatcher.IsWithin(@"C:\Watch", @"C:\WatchBackup\evidence.txt"));
    }
}
