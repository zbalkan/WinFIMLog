using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Events;
using WinFIMLog.IO;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class WindowsEventLogSinkTests
{
    [TestMethod]
    [DataRow(7777, "FileSystemFinding", "WinFIMLog", EventLogEntryType.Information)]
    [DataRow(7791, "CoverageGap", "WinFIMLog", EventLogEntryType.Error)]
    [DataRow(7794, "ConfigurationChanged", "WinFIMLog", EventLogEntryType.Warning)]
    [DataRow(7795, "BaselineFinding", "WinFIMLog", EventLogEntryType.Warning)]
    [DataRow(7797, "SecurityAuditAttribution", "WinFIMLog", EventLogEntryType.Information)]
    public void Writes_key_value_text_to_the_unified_event_log_with_the_allocated_id_and_level(
        int eventId, string recordType, string expectedSource, EventLogEntryType expectedLevel)
    {
        string? source = null;
        string? message = null;
        EventLogEntryType? level = null;
        int? writtenId = null;
        var sink = new WindowsEventLogSink((actualSource, actualMessage, actualLevel, actualId) =>
            (source, message, level, writtenId) = (actualSource, actualMessage, actualLevel, actualId));
        var record = EventContract.Create((ushort)eventId, recordType, "record", "scope",
            new Dictionary<string, object?>());

        sink.Write(record);

        Assert.AreEqual(expectedSource, source);
        Assert.AreEqual(expectedLevel, level);
        Assert.AreEqual(eventId, writtenId);
        Assert.Contains($"Event Id: {eventId}", message!);
        Assert.Contains($"Record Type: {recordType}", message!);
        Assert.DoesNotContain("Channel:", message!);
        Assert.IsFalse(message!.Contains('{'));
    }

    [TestMethod]
    public void Missing_source_is_created_for_the_selected_event_log_before_writing()
    {
        var exists = false;
        string? createdSource = null;
        string? createdLog = null;
        var sink = new WindowsEventLogSink((_, _, _, _) => { }, _ => exists,
            _ => exists ? createdLog : null,
            (source, logName) =>
            {
                createdSource = source;
                createdLog = logName;
                exists = true;
            });

        sink.Write(EventContract.Create(7777, "FileSystemFinding", "record", "scope",
            new Dictionary<string, object?>()));

        Assert.AreEqual("WinFIMLog", createdSource);
        Assert.AreEqual("WinFIMLog", createdLog);
    }

    [TestMethod]
    public void Source_registered_to_another_event_log_is_rejected_before_writing()
    {
        var sink = new WindowsEventLogSink((_, _, _, _) => { }, _ => true,
            _ => "Application", (_, _) => { });

        Assert.Throws<System.InvalidOperationException>(() => sink.Write(EventContract.Create(7777,
            "FileSystemFinding", "record", "scope", new Dictionary<string, object?>())));
    }

    [TestMethod]
    public void Contract_factory_rejects_an_id_allocated_to_another_record_type() =>
        Assert.Throws<System.ArgumentException>(() => EventContract.Create(7790, "RegistryFinding",
            "record", "scope", new Dictionary<string, object?>()));

}
