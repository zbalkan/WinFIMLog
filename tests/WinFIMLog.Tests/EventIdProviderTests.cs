using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Utils;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class EventIdProviderTests
{
    public static IEnumerable<object[]> Categories()
    {
        yield return ["FileSystem", "Created", (ushort)7776];
        yield return ["FileSystem", "Changed", (ushort)7777];
        yield return ["FileSystem", "Deleted", (ushort)7778];
        yield return ["Registry", "Created", (ushort)7786];
        yield return ["Registry", "Changed", (ushort)7787];
        yield return ["Registry", "Deleted", (ushort)7788];
    }

    [TestMethod]
    public void Explicit_event_id_takes_precedence_over_error_fallback() =>
        Assert.AreEqual((ushort)7791,
            new EventIdProvider().ComputeEventId(LogLevel.Error, new EventId(7791), "message"));

    [TestMethod]
    [DynamicData(nameof(Categories))]
    public void Maps_change_type_and_category_to_contract_id(string type, string category, ushort expected)
    {
        var state = new Dictionary<string, object?> { ["changeType"] = type, ["category"] = category };
        Assert.AreEqual(expected, new EventIdProvider().ComputeEventId(LogLevel.Information, default, state));
    }

    [TestMethod]
    public void Unclassified_error_uses_service_error_id() =>
        Assert.AreEqual((ushort)7770,
            new EventIdProvider().ComputeEventId(LogLevel.Error, default, "message"));

    [TestMethod]
    public void Unclassified_information_uses_default_id() =>
        Assert.AreEqual((ushort)7780,
            new EventIdProvider().ComputeEventId(LogLevel.Information, default, "message"));

    [TestMethod]
    public void Task_cancellation_is_emitted_at_information_level() =>
        Assert.AreEqual(LogLevel.Information,
            EventIdEventLogLoggerProvider.EffectiveLogLevel(LogLevel.Error,
                new TaskCanceledException()));

    [TestMethod]
    public void Non_cancellation_preserves_its_original_level() =>
        Assert.AreEqual(LogLevel.Error,
            EventIdEventLogLoggerProvider.EffectiveLogLevel(LogLevel.Error,
                new InvalidOperationException()));
}
