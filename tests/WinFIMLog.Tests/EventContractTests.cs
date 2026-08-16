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

        using var json = JsonDocument.Parse(record.ToJson());
        Assert.AreEqual(EventContract.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(eventId, json.RootElement.GetProperty("eventId").GetInt32());
        Assert.AreEqual(type, json.RootElement.GetProperty("recordType").GetString());
        Assert.AreEqual("Changed", json.RootElement.GetProperty("fields").GetProperty("category").GetString());
        Assert.AreEqual(channel.ToString(), json.RootElement.GetProperty("channel").GetString());
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(2, false)]
    public void Consumers_have_an_explicit_version_rule(int version, bool expected) =>
        Assert.AreEqual(expected, EventContract.IsSupported(version));
}
