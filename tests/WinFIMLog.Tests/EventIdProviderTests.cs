using System;
using System.Collections.Generic;
using Serilog.Events;
using WinFIMLog.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

    [DataTestMethod]
    [DynamicData(nameof(Categories), DynamicDataSourceType.Method)]
    public void Maps_change_type_and_category_to_contract_id(string type, string category, ushort expected)
    {
        var properties = new[]
        {
            new LogEventProperty("changeType", new ScalarValue(type)),
            new LogEventProperty("category", new ScalarValue(category))
        };
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("", []), properties);

        Assert.AreEqual(expected, new EventIdProvider().ComputeEventId(logEvent));
    }

    [TestMethod]
    public void Discovery_and_unclassified_information_use_the_default_id()
    {
        var properties = new[]
        {
            new LogEventProperty("changeType", new ScalarValue("FileSystem")),
            new LogEventProperty("category", new ScalarValue("Discovery"))
        };
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("", []), properties);
        Assert.AreEqual((ushort)7780, new EventIdProvider().ComputeEventId(logEvent));
    }

    [TestMethod]
    public void Explicit_health_event_id_takes_precedence_over_error_fallback()
    {
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, null,
            new MessageTemplate("", []), [new LogEventProperty("EventId", new ScalarValue(7791))]);
        Assert.AreEqual((ushort)7791, new EventIdProvider().ComputeEventId(logEvent));
    }

    [TestMethod]
    public void Unclassified_error_uses_service_error_id()
    {
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, null,
            new MessageTemplate("", []), []);
        Assert.AreEqual((ushort)7770, new EventIdProvider().ComputeEventId(logEvent));
    }
}
