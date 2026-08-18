using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Configuration;
using WinFIMLog.FIM;
using WinFIMLog.IO;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class EventPathRegressionTests
{
    [TestMethod]
    public void Cached_registry_handle_path_is_retained_when_the_ETW_name_fields_are_empty()
    {
        var path = RegistryMonitorJob.CombineFullKeyName(
            @"\REGISTRY\MACHINE\SOFTWARE\Contoso", string.Empty, string.Empty);

        Assert.AreEqual(@"HKEY_LOCAL_MACHINE\SOFTWARE\Contoso", path);
    }

    [TestMethod]
    public void Registry_path_reconstruction_appends_relative_key_and_value_segments()
    {
        var path = RegistryMonitorJob.CombineFullKeyName(
            @"\REGISTRY\USER\S-1-5-21-100\Software", "Contoso", "Enabled");

        Assert.AreEqual(@"HKEY_USERS\S-1-5-21-100\Software\Contoso\Enabled", path);
    }

    [TestMethod]
    public void Registry_evidence_failure_is_represented_without_throwing()
    {
        var evidence = RegistryChange.GetEvidenceOrEmpty(() => throw new UnauthorizedAccessException());

        Assert.AreEqual(string.Empty, evidence.Value);
        Assert.AreEqual(nameof(UnauthorizedAccessException), evidence.MissingReason);
    }

    [TestMethod]
    public void Path_scope_requires_a_directory_boundary()
    {
        Assert.IsTrue(PathScopeMatcher.IsWithin(@"C:\Watch", @"C:\Watch\evidence.txt"));
        Assert.IsTrue(PathScopeMatcher.IsWithin(@"C:\Watch", @"C:\Watch"));
        Assert.IsFalse(PathScopeMatcher.IsWithin(@"C:\Watch", @"C:\WatchBackup\evidence.txt"));
    }

    [TestMethod]
    public void Per_profile_downloads_resolution_uses_redirected_or_default_existing_directory()
    {
        var paths = FileSystem.ResolveUserDownloads(
        [
            (@"C:\Users\Alice", @"D:\Redirected\AliceDownloads"),
            (@"C:\Users\Bob", null),
            (@"C:\Users\Carol", @"C:\Missing\Downloads")
        ],
        path => path is @"D:\Redirected\AliceDownloads" or @"C:\Users\Bob\Downloads");

        CollectionAssert.AreEquivalent(
            new[] { @"D:\Redirected\AliceDownloads", @"C:\Users\Bob\Downloads" }, paths.ToArray());
    }
}
