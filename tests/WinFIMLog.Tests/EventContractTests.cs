using System;
using System.Collections.Generic;
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
    public void Every_record_type_has_its_required_key_value_fields()
    {
        AssertFields("FileSystemFinding", "category", "operation", "path", "currentSizeBytes", "previousSizeBytes", "currentAcl", "previousAcl", "objectType", "renameCorrelationMethod", "renameCorrelationConfidence", "attributionStatus");
        AssertFields("RegistryFinding", "category", "operation", "key", "hive", "currentAcl", "previousAcl", "attributionStatus");
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

        var message = record.FormatEventLogMessage();
        Assert.Contains("Long: 42", message);
        Assert.Contains("Double: 1.5", message);
        Assert.Contains("Int: 7", message);
        Assert.Contains("Ushort: 7790", message);
        Assert.Contains("Ulong: 123", message);
        Assert.Contains("Bool: true", message);
    }

    [TestMethod]
    public void Fields_support_timestamp_types_used_by_event_producers()
    {
        var dateTime = new DateTime(2026, 8, 17, 12, 34, 56, DateTimeKind.Utc);
        var dateTimeOffset = new DateTimeOffset(2026, 8, 17, 12, 34, 56, TimeSpan.Zero);
        var record = EventContract.Create(7795, "BaselineFinding", "01TEST", "sha256:test",
            new Dictionary<string, object?>
            {
                ["dateTime"] = dateTime,
                ["dateTimeOffset"] = dateTimeOffset
            }, EventChannel.Baseline);

        var message = record.FormatEventLogMessage();
        Assert.Contains($"Date Time: {dateTime:O}", message);
        Assert.Contains($"Date Time Offset: {dateTimeOffset:O}", message);
    }

    [TestMethod]
    public void Event_log_message_is_human_readable_key_value_text()
    {
        var record = EventContract.Create(7777, "FileSystemFinding", "01TEST", "sha256:test",
            new Dictionary<string, object?> { ["category"] = "Changed", ["optional"] = null });

        var message = record.FormatEventLogMessage();

        Assert.StartsWith("Schema Version: 1\nEvent Id: 7777\nRecord Type: FileSystemFinding", message);
        Assert.Contains("Category: Changed", message);
        Assert.Contains("Optional: None", message);
        Assert.IsFalse(message.Contains('{'));
        Assert.DoesNotContain("\"fields\"", message);
    }

    private static void AssertFields(string recordType, params string[] names)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var name in names)
        {
            fields[name] = name.EndsWith("At", StringComparison.Ordinal) ? DateTimeOffset.UtcNow : "value";
        }

        var (eventId, channel) = recordType switch
        {
            "FileSystemFinding" => ((ushort)7777, EventChannel.Operational),
            "RegistryFinding" => ((ushort)7787, EventChannel.Operational),
            "BaselineFinding" => ((ushort)7795, EventChannel.Baseline),
            "CoverageGap" => ((ushort)7791, EventChannel.Operational),
            "Health" => ((ushort)7790, EventChannel.Operational),
            "ConfigurationChanged" => ((ushort)7794, EventChannel.Operational),
            "Aggregation" => ((ushort)7796, EventChannel.Operational),
            "SecurityAuditAttribution" => ((ushort)7797, EventChannel.Diagnostic),
            _ => throw new AssertFailedException($"No event allocation for {recordType}")
        };
        var record = EventContract.Create(eventId, recordType, "record", "scope", fields, channel);
        var message = record.FormatEventLogMessage();
        foreach (var name in names)
        {
            Assert.Contains($"{DisplayName(name)}:", message, $"{recordType}.{name} is absent");
        }
    }

    private static string DisplayName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        var wasLowerCase = false;
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index != 0 && char.IsUpper(character) && wasLowerCase)
            {
                builder.Append(' ');
            }

            builder.Append(index == 0 ? char.ToUpperInvariant(character) : character);
            wasLowerCase = char.IsLower(character);
        }

        return builder.ToString();
    }
}
