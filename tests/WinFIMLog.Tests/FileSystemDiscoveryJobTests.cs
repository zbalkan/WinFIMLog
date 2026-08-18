using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemDiscoveryJobTests
{
    [TestMethod]
    public void Processing_respects_configured_concurrency()
    {
        const int concurrency = 2;
        var active = 0;
        var peak = 0;

        FileSystemDiscoveryJob.ProcessPaths(CreatePaths(20), concurrency, _ =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref peak, current);
            Thread.Sleep(10);
            Interlocked.Decrement(ref active);
        });

        Assert.IsLessThanOrEqualTo(concurrency, peak);
        Assert.IsGreaterThanOrEqualTo(1, peak);
    }

    private static IEnumerable<string> CreatePaths(int count)
    {
        for (var index = 0; index < count; index++) yield return index.ToString();
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            var replaced = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (replaced == observed) return;
            observed = replaced;
        }
    }
}
