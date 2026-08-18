using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Events;
using WinFIMLog.IO;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class WindowsEventLogSinkTests
{
    [TestMethod]
    [DataRow(7777, "FileSystemFinding", EventChannel.Operational, "WinFIM-Operational", EventLogEntryType.Information)]
    [DataRow(7791, "CoverageGap", EventChannel.Operational, "WinFIM-Operational", EventLogEntryType.Error)]
    [DataRow(7794, "ConfigurationChanged", EventChannel.Operational, "WinFIM-Operational", EventLogEntryType.Warning)]
    [DataRow(7795, "BaselineFinding", EventChannel.Baseline, "WinFIM-Baseline", EventLogEntryType.Warning)]
    [DataRow(7797, "SecurityAuditAttribution", EventChannel.Diagnostic, "WinFIM-Diagnostic", EventLogEntryType.Information)]
    public void Writes_json_to_the_selected_event_log_with_the_allocated_id_and_level(
        int eventId, string recordType, EventChannel channel, string expectedSource, EventLogEntryType expectedLevel)
    {
        string? source = null;
        string? message = null;
        EventLogEntryType? level = null;
        int? writtenId = null;
        var sink = new WindowsEventLogSink((actualSource, actualMessage, actualLevel, actualId) =>
            (source, message, level, writtenId) = (actualSource, actualMessage, actualLevel, actualId));
        var record = EventContract.Create((ushort)eventId, recordType, "record", "scope",
            new Dictionary<string, object?>(), channel);

        sink.Write(record);

        Assert.AreEqual(expectedSource, source);
        Assert.AreEqual(expectedLevel, level);
        Assert.AreEqual(eventId, writtenId);
        using var json = JsonDocument.Parse(message!);
        Assert.AreEqual(eventId, json.RootElement.GetProperty("eventId").GetInt32());
        Assert.AreEqual(recordType, json.RootElement.GetProperty("recordType").GetString());
    }

    [TestMethod]
    public void Contract_factory_rejects_an_id_allocated_to_another_record_type() =>
        Assert.Throws<System.ArgumentException>(() => EventContract.Create(7790, "RegistryFinding",
            "record", "scope", new Dictionary<string, object?>()));

    [TestMethod]
    public void Contract_factory_rejects_an_id_on_the_wrong_channel() =>
        Assert.Throws<System.ArgumentException>(() => EventContract.Create(7795, "BaselineFinding",
            "record", "scope", new Dictionary<string, object?>()));
}
