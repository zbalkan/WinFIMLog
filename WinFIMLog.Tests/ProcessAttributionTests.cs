using System;
using WinFIMLog.FIM;
using Xunit;

namespace WinFIMLog.Tests;

public sealed class ProcessAttributionTests
{
    [Fact]
    public void Lookup_failure_returns_unavailable_observation_data()
    {
        var result = ProcessAttribution.Resolve(42, "short-lived.exe",
            _ => throw new ArgumentException("process exited"));
        Assert.Equal(AttributionStatus.Unavailable, result.Status);
        Assert.Equal("short-lived.exe", result.ProcessName);
    }

    [Fact]
    public void Successful_lookup_is_attributed()
    {
        var result = ProcessAttribution.Resolve(42, null, _ => ("writer", "DOMAIN\\user", "S-1-5-21"));
        Assert.Equal(AttributionStatus.Attributed, result.Status);
        Assert.Equal("S-1-5-21", result.UserSid);
    }
}
