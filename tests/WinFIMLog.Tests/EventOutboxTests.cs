using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Events;
using WinFIMLog.IO;
using WinFIMLog.Jobs;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class EventOutboxTests
{
    private LiteDbContext context = null!;
    private string databasePath = null!;
    private EventOutboxRepository outbox = null!;

    [TestCleanup]
    public void Cleanup()
    {
        context.Dispose();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        if (File.Exists(databasePath + "-log"))
        {
            File.Delete(databasePath + "-log");
        }
    }

    [TestMethod]
    public void Empty_record_type_is_discarded_instead_of_retried_forever()
    {
        context.EventOutbox.Insert(new EventOutboxRecord
        {
            Id = "empty",
            RecordType = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.MinValue
        });
        var logger = new RecordingLogger<EventOutboxPublisher>();
        var publisher = new EventOutboxPublisher(outbox, new FailOnceWriter(), logger);

        Assert.IsTrue(publisher.PublishReady());

        var discarded = context.EventOutbox.FindById(new BsonValue("empty"));
        Assert.IsNotNull(discarded.DeliveredAt);
        Assert.AreEqual(1, discarded.DeliveryAttempts);
        Assert.AreEqual("EmptyRecordType", discarded.LastError);
        Assert.AreSequenceEqual([LogLevel.Warning], logger.Levels);
        Assert.IsFalse(publisher.PublishReady());
    }

    [TestMethod]
    public void Failed_delivery_replays_the_same_stable_record_id()
    {
        outbox.Enqueue(Record("stable"));
        var writer = new FailOnceWriter();
        var publisher = new EventOutboxPublisher(outbox, writer, NullLogger<EventOutboxPublisher>.Instance);

        Assert.IsTrue(publisher.PublishReady());
        var pending = context.EventOutbox.FindById(new BsonValue("stable"));
        Assert.IsNull(pending.DeliveredAt);
        Assert.AreEqual(1, pending.DeliveryAttempts);
        pending.NextAttemptAt = DateTimeOffset.MinValue;
        context.EventOutbox.Update(pending);

        Assert.IsTrue(publisher.PublishReady());
        var delivered = context.EventOutbox.FindById(new BsonValue("stable"));
        Assert.IsNotNull(delivered.DeliveredAt);
        Assert.AreSequenceEqual(["stable", "stable"], writer.RecordIds);
    }

    [TestInitialize]
    public void Initialise()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-outbox-{Guid.NewGuid():N}.db");
        context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        outbox = new EventOutboxRepository(context);
    }

    [TestMethod]
    public async Task Live_outbox_admission_progresses_during_chunked_baseline_staging()
    {
        var repository = new BaselineRepository(context);
        var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        var members = Enumerable.Range(0, 2_000).Select(index => new BaselineMember
        {
            Identity = $"C:\\scope\\{index}",
            Path = $"C:\\scope\\{index}",
            NodeType = SnapshotNodeType.File,
            HashState = HashEvidenceState.Hashed,
            ContentHash = index.ToString(),
            AclState = EvidenceAvailability.Available
        }).ToArray();

        var baselineTask = Task.Run(() => repository.ReconcileAndComplete(baseline, members));
        var liveTask = Task.Run(() => Parallel.For(0, 100,
            index => outbox.Enqueue(Record($"live-{index}"))));
        await Task.WhenAll(baselineTask, liveTask);

        Assert.AreEqual(BaselineStatus.Complete, baseline.Status);
        Assert.AreEqual(100, context.EventOutbox.Count());
        Assert.AreEqual(2_000, context.BaselineMembers.Count());
    }

    [TestMethod]
    public void Outbox_persists_native_contract_fields_in_LiteDB()
    {
        outbox.Enqueue(Record("native"));

        var stored = context.EventOutbox.FindById(new BsonValue("native"));
        Assert.AreEqual(EventContract.CurrentSchemaVersion, stored.SchemaVersion);
        Assert.AreEqual((ushort)7777, stored.EventId);
        Assert.AreEqual("FileSystemFinding", stored.RecordType);
        Assert.AreEqual("scope", stored.ScopeHash);
        Assert.AreEqual("C:\\evidence.txt", stored.Fields["path"]);
        Assert.AreEqual(EventChannel.Operational, stored.Channel);
    }

    [TestMethod]
    public void Outbox_records_have_the_constructor_required_by_LiteDB()
    {
        var constructor = typeof(EventOutboxRecord).GetConstructor(Type.EmptyTypes);

        Assert.IsNotNull(constructor);
        Assert.IsTrue(constructor.IsPublic);
    }

    [TestMethod]
    public void Projection_and_outbox_are_committed_atomically()
    {
        var record = Record("atomic");
        Assert.Throws<InvalidOperationException>(() => outbox.EnqueueBatch([(record, false)], () =>
        {
            context.FileSystemChanges.Insert(new FIM.FileSystemChange { Id = "projection", Entity = "C:\\one" });
            throw new InvalidOperationException("fault injection");
        }));

        Assert.AreEqual(0, context.FileSystemChanges.Count());
        Assert.AreEqual(0, context.EventOutbox.Count());
    }

    [TestMethod]
    public void Retention_never_deletes_pending_records()
    {
        outbox.Enqueue(Record("pending"));
        outbox.Enqueue(Record("delivered"));
        var delivered = context.EventOutbox.FindById(new BsonValue("delivered"));
        delivered.DeliveredAt = DateTimeOffset.UtcNow.AddDays(-30);
        context.EventOutbox.Update(delivered);

        Assert.AreEqual(1, outbox.DeleteDeliveredBefore(DateTimeOffset.UtcNow.AddDays(-7)));
        Assert.IsNotNull(context.EventOutbox.FindById(new BsonValue("pending")));
        Assert.IsNull(context.EventOutbox.FindById(new BsonValue("delivered")));
    }

    private static EventContract Record(string id) => EventContract.Create(7777, "FileSystemFinding", id,
        "scope", new Dictionary<string, object?> { ["path"] = "C:\\evidence.txt" });

    private sealed class FailOnceWriter : IEventRecordWriter
    {
        private bool fail = true;
        public List<string> RecordIds { get; } = [];

        public void Write(EventContract record, bool error = false)
        {
            RecordIds.Add(record.RecordId);
            if (fail) { fail = false; throw new IOException("fault injection"); }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
