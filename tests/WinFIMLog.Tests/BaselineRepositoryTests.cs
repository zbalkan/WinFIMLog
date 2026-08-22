using System;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Integrity;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class BaselineRepositoryTests
{
    private LiteDbContext context = null!;
    private string databasePath = null!;
    private BaselineRepository repository = null!;

    [TestMethod]
    public void Applicability_change_supersedes_but_preserves_old_complete_baseline()
    {
        var old = repository.Begin(BaselineSource.Registry, "old-scope", "hive", algorithm: BaselineAlgorithm.RegistryV2);
        repository.ReconcileAndComplete(old, [Member("A", "one")]);

        _ = repository.Begin(BaselineSource.Registry, "new-scope", "hive", algorithm: BaselineAlgorithm.RegistryV2);

        var historical = context.Baselines.FindById(new BsonValue(old.Id));
        Assert.AreEqual(BaselineStatus.Complete, historical.Status);
        Assert.AreEqual(BaselineApplicability.Superseded, historical.Applicability);
        Assert.IsNull(repository.LatestComplete(BaselineSource.Registry, "old-scope", "hive", algorithm: BaselineAlgorithm.RegistryV2));
    }

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
    public void Compaction_bounds_complete_baseline_generations()
    {
        for (var generation = 0; generation < 5; generation++)
        {
            var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
            repository.ReconcileAndComplete(baseline, [Member("A", $"hash-{generation}")]);
            foreach (var result in repository.PendingResults())
            {
                repository.RecordDeliveryAttempt(result, true);
            }

            repository.CompactAfterCompletion(baseline, generationsToKeep: 2);
        }

        Assert.AreEqual(2, context.Baselines.Count());
        Assert.AreEqual(2, context.BaselineMembers.Count());
        Assert.IsTrue(context.Baselines.FindAll().All(x => x.Status == BaselineStatus.Complete));
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

    [TestMethod]
    public void Prior_baseline_that_fails_tpm_verification_is_not_used_for_reconciliation()
    {
        var verifiedRepository = new BaselineRepository(context, new RejectingIntegrity());
        var first = verifiedRepository.Begin(BaselineSource.FileSystem, "scope", "volume");
        verifiedRepository.ReconcileAndComplete(first, [Member("A", "one")]);

        var second = verifiedRepository.Begin(BaselineSource.FileSystem, "scope", "volume");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            verifiedRepository.ReconcileAndComplete(second, [Member("A", "two")]));

        StringAssert.Contains(exception.Message, "TPM integrity verification");
        Assert.AreEqual(BaselineStatus.Building, second.Status);
        Assert.IsEmpty(repository.Members(second.Id));
    }

    [TestMethod]
    public void Filesystem_tpm_signing_fallback_restores_prior_sha256_lineage()
    {
        AssertFallbackRestoresComparableLineage(BaselineSource.FileSystem, BaselineAlgorithm.Sha256);
    }

    [TestMethod]
    public void Registry_tpm_signing_fallback_restores_prior_registry_lineage()
    {
        AssertFallbackRestoresComparableLineage(BaselineSource.Registry, BaselineAlgorithm.RegistryV2);
    }

    [TestMethod]
    public void Compaction_retains_source_native_fallback_separately_from_tpm_lineage()
    {
        var fallback = repository.Begin(BaselineSource.FileSystem, "scope", "identity",
            algorithm: BaselineAlgorithm.Sha256);
        repository.ReconcileAndComplete(fallback, [Member("A", "one")]);

        for (var index = 0; index < 2; index++)
        {
            var tpm = repository.Begin(BaselineSource.FileSystem, "scope", "identity",
                algorithm: BaselineAlgorithm.TpmRsaPssSha256);
            repository.ReconcileAndComplete(tpm, [Member("A", $"tpm-{index}")]);
            repository.CompactAfterCompletion(tpm, generationsToKeep: 2);
        }

        Assert.IsNotNull(context.Baselines.FindById(fallback.Id));
        Assert.HasCount(1, repository.Members(fallback.Id));
    }

    [TestMethod]
    public void Duplicate_identity_cannot_be_published_as_complete()
    {
        var baseline = repository.Begin(BaselineSource.FileSystem, "scope", "volume");
        Assert.Throws<InvalidOperationException>(() => repository.ReconcileAndComplete(baseline,
            [Member("A", "one"), Member("a", "two")]));
        Assert.AreEqual(BaselineStatus.Building, context.Baselines.FindById(new BsonValue(baseline.Id)).Status);
    }

    [TestInitialize]
    public void Initialise()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-{Guid.NewGuid():N}.db");
        context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        repository = new BaselineRepository(context);
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
    public void Resolved_hive_manifest_change_starts_lineage_without_mass_deletion()
    {
        var first = repository.Begin(BaselineSource.Registry, "scope", "HKEY_USERS\\S-1-1",
            algorithm: BaselineAlgorithm.RegistryV2);
        repository.ReconcileAndComplete(first, [Member("HKEY_USERS\\S-1-1\\RUN", "one")]);

        var second = repository.Begin(BaselineSource.Registry, "scope", "HKEY_USERS\\S-1-2",
            algorithm: BaselineAlgorithm.RegistryV2);
        var results = repository.ReconcileAndComplete(second,
            [Member("HKEY_USERS\\S-1-2\\RUN", "two")]);

        Assert.IsEmpty(results);
        Assert.AreEqual(BaselineApplicability.Superseded,
            context.Baselines.FindById(new BsonValue(first.Id)).Applicability);
        Assert.AreEqual(BaselineApplicability.Current, second.Applicability);
    }

    private void AssertFallbackRestoresComparableLineage(BaselineSource source, BaselineAlgorithm fallbackAlgorithm)
    {
        var first = repository.Begin(source, "scope", "identity", algorithm: fallbackAlgorithm);
        repository.ReconcileAndComplete(first, [Member("A", "one")]);

        var tpmCandidate = repository.Begin(source, "scope", "identity",
            algorithm: BaselineAlgorithm.TpmRsaPssSha256);
        Assert.IsNull(repository.LatestComplete(source, "scope", "identity", algorithm: fallbackAlgorithm));

        tpmCandidate.Algorithm = fallbackAlgorithm;
        repository.RestoreFallbackApplicability(tpmCandidate);
        var results = repository.ReconcileAndComplete(tpmCandidate, [Member("A", "two")]);

        Assert.HasCount(1, results);
        Assert.AreEqual(ReconciliationChange.Changed, results[0].Change);
        Assert.AreEqual(first.Id, results[0].PreviousBaselineId);
    }

    private sealed class RejectingIntegrity : ITpmBaselineIntegrity
    {
        public bool TryPrepare(out string reason) { reason = "NotUsed"; return false; }

        public bool TrySeal(BaselineMetadata baseline, System.Collections.Generic.IReadOnlyCollection<BaselineMember> members,
            out string reason) { reason = "NotUsed"; return false; }

        public bool TryVerify(BaselineMetadata baseline, System.Collections.Generic.IReadOnlyCollection<BaselineMember> members,
            out string reason) { reason = "RejectedForTest"; return false; }
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
