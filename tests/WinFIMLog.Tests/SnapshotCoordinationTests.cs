using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Health;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class SnapshotCoordinationTests
{
    [TestMethod]
    public void Recovery_storm_is_coalesced_to_one_request_per_source()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-snapshot-requests-{Guid.NewGuid():N}.db");
        using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        try
        {
            var settings = new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath()));
            var repository = new BaselineRepository(context);
            var service = new SnapshotService(repository, settings,
                NullLogger<SnapshotService>.Instance, new RecordingHealth(), new SnapshotHealthState(),
                Options.Create(new RetentionOptions()), new FileSystemBaselineAvailability(repository, settings));

            for (var index = 0; index < 10_000; index++)
            {
                service.RequestFileSystemSnapshot($"overflow-{index}", "C:\\scope");
                service.RequestRegistrySnapshot($"loss-{index}", "HKLM");
            }

            Assert.AreEqual(1, service.PendingFileSystemRequests);
            Assert.AreEqual(1, service.PendingRegistryRequests);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            if (File.Exists(databasePath + "-log"))
            {
                File.Delete(databasePath + "-log");
            }
        }
    }

    [TestMethod]
    public void Snapshot_retry_is_exponential_and_bounded_below_the_periodic_interval()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(1), SnapshotService.RetryDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(8), SnapshotService.RetryDelay(4));
        Assert.AreEqual(TimeSpan.FromSeconds(256), SnapshotService.RetryDelay(20));
        Assert.IsTrue(SnapshotService.RetryDelay(20) < TimeSpan.FromHours(6));
    }

    private sealed class RecordingHealth : IHealthReporter
    {
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1)
        { }

        public void SinkFailure(string sink, string reason, int attempt)
        { }

        public void SourceRecovered(string source, string scope, string action)
        { }
    }
}
