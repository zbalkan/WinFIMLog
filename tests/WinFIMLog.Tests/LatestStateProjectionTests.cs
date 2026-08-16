using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Health;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class LatestStateProjectionTests
{
    [TestMethod]
    public async System.Threading.Tasks.Task Every_event_enters_outbox_but_projection_keeps_latest_entity_state()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-projection-{Guid.NewGuid():N}.db");
        using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        try
        {
            var fileBuffer = new FileSystemChangeBuffer();
            var registryBuffer = new RegistryChangeBuffer();
            await fileBuffer.Add(Change("first", "C:\\Evidence.txt", DateTime.UtcNow.AddSeconds(-1), "one"));
            await fileBuffer.Add(Change("second", "c:\\evidence.txt", DateTime.UtcNow, "two"));
            var consumer = new BufferConsumer(NullLogger<JobOrchestrator>.Instance,
                fileBuffer, registryBuffer, context,
                new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", "C:\\")),
                new RecordingHealth(), new EventOutboxRepository(context));

            Assert.IsTrue(consumer.ProcessChanges());

            Assert.AreEqual(1, context.FileSystemChanges.Count());
            Assert.AreEqual("second", context.FileSystemChanges.FindOne(x => x.Entity == "c:\\evidence.txt").Id);
            Assert.AreEqual(2, context.EventOutbox.Count());
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(databasePath + "-log")) File.Delete(databasePath + "-log");
        }
    }

    private static FileSystemChange Change(string id, string entity, DateTime when, string hash) => new()
    {
        Id = id,
        Entity = entity,
        FullPath = entity,
        DateTime = when,
        ScopeHash = "scope",
        ChangeCategory = ChangeCategory.Changed,
        CurrentHash = hash,
        PreviousHash = string.Empty,
        ACLs = string.Empty,
        SourceComputer = "test"
    };

    private sealed class RecordingHealth : IHealthReporter
    {
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1) { }
        public void SourceRecovered(string source, string scope, string action) { }
        public void SinkFailure(string sink, string reason, int attempt) => Assert.Fail($"Unexpected sink failure: {sink}/{reason}");
    }
}
