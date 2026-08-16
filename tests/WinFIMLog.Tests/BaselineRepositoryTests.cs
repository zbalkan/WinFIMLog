using System;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class BaselineRepositoryTests
{
    private string databasePath = null!;
    private LiteDbContext context = null!;
    private BaselineRepository repository = null!;

    [TestInitialize]
    public void Initialise()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-{Guid.NewGuid():N}.db");
        context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        repository = new BaselineRepository(context);
    }

    [TestCleanup]
    public void Cleanup()
    {
        context.Dispose();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        if (File.Exists(databasePath + "-log")) File.Delete(databasePath + "-log");
    }

    [TestMethod]
    public void Complete_baseline_is_atomic_and_subsequent_diff_records_all_change_kinds()
    {
        var first = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        Assert.IsEmpty(repository.ReconcileAndComplete(first,
        [
            Member("A", "hash-a"),
            Member("B", "hash-b")
        ]));

        var second = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        var results = repository.ReconcileAndComplete(second,
        [
            Member("A", "hash-a2"),
            Member("C", "hash-c")
        ]);

        Assert.AreEqual(BaselineStatus.Complete, second.Status);
        Assert.AreEqual(2L, second.ItemCount);
        Assert.IsNotNull(second.CompletedAt);
        Assert.AreSequenceEqual(
            [ReconciliationChange.Changed, ReconciliationChange.Created, ReconciliationChange.Deleted], results
                .Select(x => x.Change), SequenceOrder.InAnyOrder);
        Assert.HasCount(2, repository.Members(second.Id));
        Assert.AreEqual(second.Id, repository.Find(second.Id)?.Id);
        Assert.HasCount(3, repository.PendingResults());
        repository.RecordDeliveryAttempt(results[0], true);
        Assert.HasCount(2, repository.PendingResults());
        Assert.IsNotNull(context.ReconciliationResults.FindById(new BsonValue(results[0].Id)).DeliveredAt);
    }

    [TestMethod]
    public void Applicability_change_supersedes_but_preserves_old_complete_baseline()
    {
        var old = repository.Begin(BaselineSource.Registry, "old-scope", "hive", algorithmVersion: "registry-v1");
        repository.ReconcileAndComplete(old, [Member("A", "one")]);

        _ = repository.Begin(BaselineSource.Registry, "new-scope", "hive", algorithmVersion: "registry-v1");

        var historical = context.Baselines.FindById(new BsonValue(old.Id));
        Assert.AreEqual(BaselineStatus.Complete, historical.Status);
        Assert.AreEqual(BaselineApplicability.Superseded, historical.Applicability);
        Assert.IsNull(repository.LatestComplete(BaselineSource.Registry, "old-scope", "hive", algorithmVersion: "registry-v1"));
    }

    [TestMethod]
    public void Interrupted_baseline_is_never_returned_as_complete()
    {
        var interrupted = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        repository.MarkInvalid(interrupted, "cancelled");

        Assert.AreEqual(BaselineStatus.Invalid, interrupted.Status);
        Assert.IsNull(repository.LatestComplete(BaselineSource.FileSystem, "scope", "volume"));
    }

    [TestMethod]
    public void Duplicate_identity_cannot_be_published_as_complete()
    {
        var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        Assert.Throws<InvalidOperationException>(() => repository.ReconcileAndComplete(baseline,
            [Member("A", "one"), Member("a", "two")]));
        Assert.AreEqual(BaselineStatus.Building, context.Baselines.FindById(new BsonValue(baseline.Id)).Status);
    }

    [TestMethod]
    public void Compaction_bounds_complete_baseline_generations()
    {
        for (var generation = 0; generation < 5; generation++)
        {
            var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
            repository.ReconcileAndComplete(baseline, [Member("A", $"hash-{generation}")]);
            foreach (var result in repository.PendingResults()) repository.RecordDeliveryAttempt(result, true);
            repository.CompactAfterCompletion(baseline, generationsToKeep: 2);
        }

        Assert.AreEqual(2, context.Baselines.Count());
        Assert.AreEqual(2, context.BaselineMembers.Count());
        Assert.IsTrue(context.Baselines.FindAll().All(x => x.Status == BaselineStatus.Complete));
    }

    [TestMethod]
    public void Resolved_hive_manifest_change_starts_lineage_without_mass_deletion()
    {
        var first = repository.Begin(BaselineSource.Registry, "scope", "HKEY_USERS\\S-1-1",
            algorithmVersion: "registry-v1");
        repository.ReconcileAndComplete(first, [Member("HKEY_USERS\\S-1-1\\RUN", "one")]);

        var second = repository.Begin(BaselineSource.Registry, "scope", "HKEY_USERS\\S-1-2",
            algorithmVersion: "registry-v1");
        var results = repository.ReconcileAndComplete(second,
            [Member("HKEY_USERS\\S-1-2\\RUN", "two")]);

        Assert.IsEmpty(results);
        Assert.AreEqual(BaselineApplicability.Superseded,
            context.Baselines.FindById(new BsonValue(first.Id)).Applicability);
        Assert.AreEqual(BaselineApplicability.Current, second.Applicability);
    }

    [TestMethod]
    public void Consistency_evidence_is_committed_with_complete_metadata()
    {
        var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        baseline.ConsistencyMethod = "CursorlessConsecutiveAgreement";
        baseline.ObservationPasses = 3;
        repository.ReconcileAndComplete(baseline, [Member("A", "hash")]);

        var persisted = context.Baselines.FindById(new BsonValue(baseline.Id));
        Assert.AreEqual("CursorlessConsecutiveAgreement", persisted.ConsistencyMethod);
        Assert.AreEqual(3, persisted.ObservationPasses);
    }

    private static BaselineMember Member(string identity, string hash) => new()
    {
        Identity = identity,
        Path = identity,
        NodeType = SnapshotNodeType.File,
        ContentHash = hash,
        HashState = HashEvidenceState.Hashed,
        AclState = EvidenceAvailability.Available
    };
}
