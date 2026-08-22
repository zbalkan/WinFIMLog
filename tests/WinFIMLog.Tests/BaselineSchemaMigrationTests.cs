using System;
using System.IO;
using LiteDB;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class BaselineSchemaMigrationTests
{
    [TestMethod]
    public void Legacy_algorithm_and_evidence_strings_are_migrated_to_enums_and_binary_values()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-baseline-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = new LiteDatabase(databasePath))
            {
                var baselines = database.GetCollection<BsonDocument>("baselines");
                baselines.Insert(new BsonDocument
                {
                    ["_id"] = "legacy-baseline",
                    ["AlgorithmVersion"] = "sha256-v1+tpm-rsa-pss-v1",
                    ["IntegrityAlgorithm"] = "tpm-rsa-pss-sha256-v1",
                    ["IntegrityManifestHash"] = Convert.ToHexString([1, 2, 3]),
                    ["IntegrityPublicKey"] = Convert.ToBase64String([4, 5, 6]),
                    ["IntegritySignature"] = Convert.ToBase64String([7, 8, 9]),
                    ["IntegrityKeyName"] = "legacy-key",
                    ["Source"] = (int)BaselineSource.FileSystem,
                    ["ScopeHash"] = "scope",
                    ["SourceIdentity"] = "volume",
                    ["SchemaVersion"] = 1,
                    ["Status"] = (int)BaselineStatus.Complete,
                    ["Applicability"] = (int)BaselineApplicability.Current
                });
            }

            using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
            var baseline = context.Baselines.FindById("legacy-baseline");

            Assert.IsNotNull(baseline);
            Assert.AreEqual(BaselineAlgorithm.TpmRsaPssSha256, baseline.Algorithm);
            Assert.AreEqual(BaselineAlgorithm.TpmRsaPssSha256, baseline.IntegrityAlgorithm);
            Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, baseline.IntegrityManifestHash);
            Assert.AreSequenceEqual(new byte[] { 4, 5, 6 }, baseline.IntegrityPublicKey);
            Assert.AreSequenceEqual(new byte[] { 7, 8, 9 }, baseline.IntegritySignature);
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
    public void Registry_v1_baseline_is_preserved_as_invalid_history_not_promoted_to_v2()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-baseline-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (var database = new LiteDatabase(databasePath))
            {
                database.GetCollection<BsonDocument>("baselines").Insert(new BsonDocument
                {
                    ["_id"] = "legacy-registry-v1",
                    ["AlgorithmVersion"] = "registry-v1",
                    ["Source"] = (int)BaselineSource.Registry,
                    ["ScopeHash"] = "scope",
                    ["SourceIdentity"] = "hive",
                    ["SchemaVersion"] = 1,
                    ["Status"] = (int)BaselineStatus.Complete,
                    ["Applicability"] = (int)BaselineApplicability.Current
                });
            }

            using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
            var baseline = context.Baselines.FindById("legacy-registry-v1");
            Assert.IsNotNull(baseline);
            Assert.AreEqual(BaselineStatus.Invalid, baseline.Status);
            Assert.Contains("not comparable", baseline.InvalidReason);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static void DeleteDatabase(string databasePath)
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
