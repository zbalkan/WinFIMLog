using System;
using WinFIMLog.FIM;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class ProcessAttributionTests
{
    [TestMethod]
    public void Lookup_failure_returns_unavailable_observation_data()
    {
        var result = ProcessAttribution.Resolve(42, "short-lived.exe",
            _ => throw new ArgumentException("process exited"));
        Assert.AreEqual(AttributionStatus.Unavailable, result.Status);
        Assert.AreEqual("short-lived.exe", result.ProcessName);
    }

    [TestMethod]
    public void Successful_lookup_is_attributed()
    {
        var result = ProcessAttribution.Resolve(42, null, _ => ("writer", "DOMAIN\\user", "S-1-5-21"));
        Assert.AreEqual(AttributionStatus.Attributed, result.Status);
        Assert.AreEqual("S-1-5-21", result.UserSid);
    }
}
