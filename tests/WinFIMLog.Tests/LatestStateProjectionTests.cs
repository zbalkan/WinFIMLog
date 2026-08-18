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
    public async System.Threading.Tasks.Task Rename_replaces_the_old_path_projection_and_enters_the_outbox()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-rename-{Guid.NewGuid():N}.db");
        using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        try
        {
            context.FileSystemChanges.Insert(Change("old", "C:\\old.txt", DateTime.UtcNow.AddSeconds(-1), "same"));
            var fileBuffer = new FileSystemChangeBuffer();
            var renamed = Change("rename", "C:\\new.txt", DateTime.UtcNow, "same");
            renamed.OldPath = "C:\\old.txt";
            renamed.NewPath = "C:\\new.txt";
            await fileBuffer.Add(renamed);
            var consumer = new BufferConsumer(NullLogger<JobOrchestrator>.Instance,
                fileBuffer, new RegistryChangeBuffer(), context,
                new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", "C:\\")),
                new RecordingHealth(), new EventOutboxRepository(context));

            Assert.IsTrue(consumer.ProcessChanges());

            Assert.IsFalse(context.FileSystemChanges.Exists(x => x.Entity == "C:\\old.txt"));
            Assert.IsTrue(context.FileSystemChanges.Exists(x => x.Entity == "C:\\new.txt"));
            var record = context.EventOutbox.FindById("rename");
            Assert.AreEqual((ushort)7777, record.EventId);
            Assert.AreEqual("RenamedOrMoved", record.Fields["operation"]);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(databasePath + "-log")) File.Delete(databasePath + "-log");
        }
    }

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
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1)
        { }

        public void SinkFailure(string sink, string reason, int attempt) => Assert.Fail($"Unexpected sink failure: {sink}/{reason}");

        public void SourceRecovered(string source, string scope, string action)
        { }
    }
}
