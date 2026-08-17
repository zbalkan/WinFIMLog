using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Events;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class EventContractTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    public void Consumers_have_an_explicit_version_rule(int version, bool expected) =>
        Assert.AreEqual(expected, EventContract.IsSupported(version));

    [TestMethod]
    public void Every_record_type_has_its_required_machine_readable_fields()
    {
        AssertFields("FileSystemFinding", "category", "path", "objectType", "attributionStatus");
        AssertFields("RegistryFinding", "category", "key", "hive", "attributionStatus");
        AssertFields("BaselineFinding", "baselineId", "source", "change", "identity", "detectedAt");
        AssertFields("CoverageGap", "source", "scope", "reason", "lostCount");
        AssertFields("Health", "queueDepth", "oldestItemAgeMs", "accepted", "processed", "dropped", "enrichmentFailures");
        AssertFields("ConfigurationChanged", "previousScopeHash", "newScopeHash");
        AssertFields("Aggregation", "sourceEventId", "groupKey", "count", "windowStartedAt", "windowEndedAt", "sampleRecordId");
        AssertFields("SecurityAuditAttribution", "nativeEventId", "subjectUserSid", "objectName", "nativeEvidence");
    }

    [TestMethod]
    public void Fields_support_all_numeric_scalar_types_used_by_event_producers()
    {
        var record = EventContract.Create(7790, "Health", "01TEST", "sha256:test",
            new Dictionary<string, object?>
            {
                ["long"] = 42L,
                ["double"] = 1.5D,
                ["int"] = 7,
                ["ushort"] = (ushort)7790,
                ["ulong"] = 123UL,
                ["bool"] = true
            });

        using var json = JsonDocument.Parse(record.FormatEventLogMessage());
        var fields = json.RootElement.GetProperty("fields");
        Assert.AreEqual(42L, fields.GetProperty("long").GetInt64());
        Assert.AreEqual(1.5D, fields.GetProperty("double").GetDouble());
        Assert.AreEqual(7, fields.GetProperty("int").GetInt32());
        Assert.AreEqual(7790, fields.GetProperty("ushort").GetUInt16());
        Assert.AreEqual(123UL, fields.GetProperty("ulong").GetUInt64());
        Assert.IsTrue(fields.GetProperty("bool").GetBoolean());
    }

    [TestMethod]
    [DataRow(7776, "FileSystemFinding", EventChannel.Operational)]
    [DataRow(7787, "RegistryFinding", EventChannel.Operational)]
    [DataRow(7795, "BaselineFinding", EventChannel.Baseline)]
    [DataRow(7791, "CoverageGap", EventChannel.Operational)]
    [DataRow(7790, "Health", EventChannel.Operational)]
    [DataRow(7794, "ConfigurationChanged", EventChannel.Operational)]
    [DataRow(7796, "Aggregation", EventChannel.Operational)]
    public void Schema_fixture_is_machine_readable(int eventId, string type, EventChannel channel)
    {
        var record = EventContract.Create((ushort)eventId, type, "01TEST", "sha256:test",
            new Dictionary<string, object?> { ["category"] = "Changed", ["optional"] = null }, channel);

        using var json = JsonDocument.Parse(record.FormatEventLogMessage());
        Assert.AreEqual(EventContract.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(eventId, json.RootElement.GetProperty("eventId").GetInt32());
        Assert.AreEqual(type, json.RootElement.GetProperty("recordType").GetString());
        Assert.AreEqual("Changed", json.RootElement.GetProperty("fields").GetProperty("category").GetString());
        Assert.AreEqual(channel.ToString(), json.RootElement.GetProperty("channel").GetString());
    }

    private static void AssertFields(string recordType, params string[] names)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var name in names) fields[name] = name.EndsWith("At", StringComparison.Ordinal) ? DateTimeOffset.UtcNow : "value";
        var record = EventContract.Create(7790, recordType, "record", "scope", fields);
        using var json = JsonDocument.Parse(record.FormatEventLogMessage());
        foreach (var name in names)
            Assert.IsTrue(json.RootElement.GetProperty("fields").TryGetProperty(name, out _), $"{recordType}.{name} is absent");
    }
}
