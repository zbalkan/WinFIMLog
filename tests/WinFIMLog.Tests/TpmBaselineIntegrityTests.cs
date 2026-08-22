using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Integrity;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class TpmBaselineIntegrityTests
{
    [TestMethod]
    public void Manifest_hash_commits_the_persisted_member_count()
    {
        var baseline = Baseline(itemCount: 1);
        BaselineMember[] members = [Member("C:\\scope\\evidence.txt", "AA")];
        var hash = TpmBaselineIntegrity.ComputeManifestHash(baseline, members);

        baseline.ItemCount = 2;
        var changedCountHash = TpmBaselineIntegrity.ComputeManifestHash(baseline, members);

        Assert.IsFalse(hash.SequenceEqual(changedCountHash));
    }

    [TestMethod]
    public void Tpm_claiming_baseline_with_missing_signature_is_rejected()
    {
        var baseline = Baseline(itemCount: 0);
        baseline.IntegrityAlgorithm = TpmBaselineIntegrity.Algorithm;
        var integrity = new TpmBaselineIntegrity(new Settings(
            SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath())));

        Assert.IsFalse(integrity.TryVerify(baseline, Array.Empty<BaselineMember>(), out var reason));
        StringAssert.Contains(reason, "missing its required integrity signature");
    }

    [TestMethod]
    public void Legacy_sha_only_baseline_without_signature_remains_compatible()
    {
        var baseline = Baseline(itemCount: 0);
        baseline.Algorithm = BaselineAlgorithm.Sha256;
        var integrity = new TpmBaselineIntegrity(new Settings(
            SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath())));

        Assert.IsTrue(integrity.TryVerify(baseline, Array.Empty<BaselineMember>(), out var reason));
        Assert.AreEqual(string.Empty, reason);
    }

    [TestMethod]
    public void Manifest_hash_is_deterministic_regardless_of_member_input_order()
    {
        var baseline = Baseline(itemCount: 2);
        var left = TpmBaselineIntegrity.ComputeManifestHash(baseline,
            [Member("C:\\scope\\b.txt", "BB"), Member("C:\\scope\\a.txt", "AA")]);
        var right = TpmBaselineIntegrity.ComputeManifestHash(baseline,
            [Member("C:\\scope\\a.txt", "AA"), Member("C:\\scope\\b.txt", "BB")]);

        CollectionAssert.AreEqual(left, right);
    }

    [TestMethod]
    public void Manifest_distinguishes_null_registry_data_from_empty_registry_data()
    {
        var baseline = Baseline(itemCount: 1);
        var missing = Member("HKLM\\Value", "AA");
        missing.RegistryValueData = null;
        var empty = Member("HKLM\\Value", "AA");
        empty.RegistryValueData = [];

        var missingHash = TpmBaselineIntegrity.ComputeManifestHash(baseline, [missing]);
        var emptyHash = TpmBaselineIntegrity.ComputeManifestHash(baseline, [empty]);

        Assert.IsFalse(missingHash.SequenceEqual(emptyHash));
    }

    [TestMethod]
    public void Retirement_without_a_provisioned_key_does_not_require_a_tpm_provider()
    {
        var settings = new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath()));

        Assert.IsTrue(TpmBaselineIntegrity.TryRetire(settings, out var reason));
        Assert.AreEqual(string.Empty, reason);
    }

    private static BaselineMetadata Baseline(long itemCount) => new()
    {
        Id = "01JTPMINTEGRITYBASELINE000001",
        Source = BaselineSource.FileSystem,
        ScopeHash = "scope",
        SourceIdentity = "volume",
        SchemaVersion = 1,
        Algorithm = BaselineAlgorithm.TpmRsaPssSha256,
        ConsistencyMethod = "CursorlessConsecutiveAgreement",
        ObservationPasses = 2,
        ItemCount = itemCount
    };

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
