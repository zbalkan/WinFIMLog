using System;
using System.IO;
using LiteDB;
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
    public void Legacy_projections_are_migrated_only_once()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-projection-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = new LiteDatabase(databasePath))
            {
                var older = FileChange("older-file", "C:\\Evidence.txt", DateTime.UtcNow.AddMinutes(-1), "old");
                var newer = FileChange("newer-file", "c:\\evidence.txt", DateTime.UtcNow, "new");
                older.NormalizedEntity = string.Empty;
                newer.NormalizedEntity = string.Empty;
                database.GetCollection<FileSystemChange>("fileSystemChanges").InsertBulk([older, newer]);

                var olderRegistry = RegistryChange("older-registry", "HKEY_LOCAL_MACHINE\\Software\\Vendor", DateTime.UtcNow.AddMinutes(-1));
                var newerRegistry = RegistryChange("newer-registry", "hkey_local_machine\\software\\vendor", DateTime.UtcNow);
                olderRegistry.NormalizedEntity = string.Empty;
                newerRegistry.NormalizedEntity = string.Empty;
                database.GetCollection<RegistryChange>("registryChanges").InsertBulk([olderRegistry, newerRegistry]);
            }

            using (var migrated = CreateContext(databasePath))
            {
                Assert.IsTrue(migrated.LatestStateMigrationPerformed);
                Assert.AreEqual(1, migrated.FileSystemChanges.Count());
                Assert.AreEqual(1, migrated.RegistryChanges.Count());
                Assert.AreEqual("newer-file", migrated.FileSystemChanges.FindOne(
                    x => x.NormalizedEntity == "C:\\EVIDENCE.TXT").Id);
                Assert.AreEqual("newer-registry", migrated.RegistryChanges.FindOne(
                    x => x.NormalizedEntity == "HKEY_LOCAL_MACHINE\\SOFTWARE\\VENDOR").Id);
            }

            using var reopened = CreateContext(databasePath);
            Assert.IsFalse(reopened.LatestStateMigrationPerformed);
            Assert.AreEqual(1, reopened.FileSystemChanges.Count());
            Assert.AreEqual(1, reopened.RegistryChanges.Count());
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Rename_replaces_the_old_path_projection_and_enters_the_outbox()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-rename-{Guid.NewGuid():N}.db");
        using var context = CreateContext(databasePath);
        try
        {
            context.FileSystemChanges.Insert(FileChange("old", "C:\\old.txt", DateTime.UtcNow.AddSeconds(-1), "same"));
            var fileBuffer = new FileSystemChangeBuffer();
            var renamed = FileChange("rename", "C:\\new.txt", DateTime.UtcNow, "same");
            renamed.OldPath = "C:\\old.txt";
            renamed.NewPath = "C:\\new.txt";
            await fileBuffer.Add(renamed);
            var consumer = Consumer(fileBuffer, new RegistryChangeBuffer(), context);

            Assert.IsTrue(consumer.ProcessChanges());

            Assert.IsFalse(context.FileSystemChanges.Exists(x => x.Entity == "C:\\old.txt"));
            Assert.IsTrue(context.FileSystemChanges.Exists(x => x.Entity == "C:\\new.txt"));
            var record = context.EventOutbox.FindById("rename");
            Assert.AreEqual((ushort)7777, record.EventId);
            Assert.AreEqual("RenamedOrMoved", record.Fields["operation"]);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Every_event_enters_outbox_but_projection_keeps_latest_entity_state()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-projection-{Guid.NewGuid():N}.db");
        using var context = CreateContext(databasePath);
        try
        {
            var fileBuffer = new FileSystemChangeBuffer();
            var registryBuffer = new RegistryChangeBuffer();
            await fileBuffer.Add(FileChange("first", "C:\\Evidence.txt", DateTime.UtcNow.AddSeconds(-1), "one"));
            await fileBuffer.Add(FileChange("second", "c:\\evidence.txt", DateTime.UtcNow, "two"));
            var consumer = Consumer(fileBuffer, registryBuffer, context);

            Assert.IsTrue(consumer.ProcessChanges());

            Assert.AreEqual(1, context.FileSystemChanges.Count());
            Assert.AreEqual("second", context.FileSystemChanges.FindOne(x => x.Entity == "c:\\evidence.txt").Id);
            Assert.AreEqual(2, context.EventOutbox.Count());
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [TestMethod]
    public async System.Threading.Tasks.Task Projection_replaces_one_indexed_entity_without_changing_unrelated_rows()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-projection-{Guid.NewGuid():N}.db");
        try
        {
            using (var seed = CreateContext(databasePath))
            {
                seed.FileSystemChanges.Insert(FileChange("old", "C:\\Evidence.txt", DateTime.UtcNow.AddMinutes(-1), "old"));
                seed.FileSystemChanges.Insert(FileChange("other", "C:\\Other.txt", DateTime.UtcNow, "other"));
            }

            using var context = CreateContext(databasePath);
            var fileBuffer = new FileSystemChangeBuffer();
            await fileBuffer.Add(FileChange("new", "c:\\evidence.txt", DateTime.UtcNow, "new"));
            var consumer = Consumer(fileBuffer, new RegistryChangeBuffer(), context);

            Assert.IsTrue(consumer.ProcessChanges());
            Assert.AreEqual(2, context.FileSystemChanges.Count());
            Assert.IsNotNull(context.FileSystemChanges.FindById("other"));
            Assert.AreEqual("new", context.FileSystemChanges.FindOne(
                x => x.NormalizedEntity == "C:\\EVIDENCE.TXT").Id);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static BufferConsumer Consumer(FileSystemChangeBuffer fileBuffer, RegistryChangeBuffer registryBuffer,
        ILiteDbContext context) => new(NullLogger<JobOrchestrator>.Instance, fileBuffer, registryBuffer, context,
        new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", "C:\\")),
        new RecordingHealth(), new EventOutboxRepository(context));

    private static LiteDbContext CreateContext(string databasePath) =>
        new(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));

    private static FileSystemChange FileChange(string id, string entity, DateTime when, string hash) => new()
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

    private static RegistryChange RegistryChange(string id, string entity, DateTime when) => new()
    {
        Id = id,
        Entity = entity,
        DateTime = when,
        ScopeHash = "scope",
        ConfigChangeType = ConfigChangeType.Registry,
        ChangeCategory = ChangeCategory.Changed,
        ACLs = string.Empty,
        Hive = "LocalMachine",
        KeyName = entity,
        SourceComputer = "test"
    };

    private static void DeleteDatabase(string databasePath)
    {
        if (File.Exists(databasePath)) File.Delete(databasePath);
        if (File.Exists(databasePath + "-log")) File.Delete(databasePath + "-log");
    }

    private sealed class RecordingHealth : IHealthReporter
    {
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1)
        { }

        public void SinkFailure(string sink, string reason, int attempt) => Assert.Fail($"Unexpected sink failure: {sink}/{reason}");

        public void SourceRecovered(string source, string scope, string action)
        { }
    }
}
