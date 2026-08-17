using System;
using System.IO;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemBaselineAvailabilityTests
{
    [TestMethod]
    public void Notifications_are_suppressed_only_until_the_first_baseline_completes()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-baseline-availability-{Guid.NewGuid():N}.db");
        using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        try
        {
            var settings = new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath()));
            var configuration = settings.Capture();
            var repository = new BaselineRepository(context);
            var availability = new FileSystemBaselineAvailability(repository, settings);

            Assert.IsFalse(availability.IsEstablished(configuration));

            var first = repository.Begin(BaselineSource.FileSystem, configuration.ScopeHash,
                SourceIdentityProvider.FileSystem(configuration.MonitoredPaths));
            availability.Refresh(configuration);
            Assert.IsFalse(availability.IsEstablished(configuration));

            repository.ReconcileAndComplete(first, Array.Empty<BaselineMember>());
            availability.Refresh(configuration);
            Assert.IsTrue(availability.IsEstablished(configuration));

            _ = repository.Begin(BaselineSource.FileSystem, configuration.ScopeHash,
                SourceIdentityProvider.FileSystem(configuration.MonitoredPaths));
            availability.Refresh(configuration);
            Assert.IsTrue(availability.IsEstablished(configuration),
                "A subsequent scan must not suppress watcher notifications while it is running.");
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
}
