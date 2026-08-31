using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.USN;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnReplayCoordinationTests
{
    [TestMethod]
    public void Gap_report_storm_is_coalesced_to_one_replay_request()
    {
        // A burst of coverage-gap reports (watcher overflow, queue shedding, etc.) from multiple
        // volumes and scopes all describe one time window. The channel is bounded at 1, and drops
        // writes when it is full, because a pending request already covers everything a later one
        // would ask for. Multiple threads writing concurrently must coalesce, not queue.
        var coordinator = new UsnReplayCoordinator();

        // Simulate a burst of gap reports: 10,000 requests from different reasons and scopes.
        for (var index = 0; index < 10_000; index++)
        {
            coordinator.RequestReplay($"overflow-{index}", $"C:\\scope-{index % 100}");
        }

        Assert.AreEqual(1, coordinator.Pending,
            "A burst of replay requests should coalesce to exactly one pending request");
    }

    [TestMethod]
    public async Task Pending_request_is_consumed_without_blocking()
    {
        // When a request is pending and the worker is ready, ReadAsync returns immediately.
        // There is no back-pressure: writes are dropped when the channel is full, not queued.
        var coordinator = new UsnReplayCoordinator();

        coordinator.RequestReplay("TestReason", "C:\\test");
        Assert.AreEqual(1, coordinator.Pending);

        // The read should complete immediately.
        using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(1));
        var request = await coordinator.ReadAsync(cts.Token);

        Assert.AreEqual("TestReason", request.Reason);
        Assert.AreEqual("C:\\test", request.AffectedScope);
        Assert.AreEqual(0, coordinator.Pending, "The request should be consumed");
    }

    [TestMethod]
    public void Dropped_writes_allow_concurrent_requests_to_proceed()
    {
        // Once a request is pending, further writes fail silently. This prevents blocking the
        // watcher or capture-queue threads. The pending request already covers the gap; a later
        // request that drops just means one less reason listed in the log.
        var coordinator = new UsnReplayCoordinator();

        coordinator.RequestReplay("Request1", "C:\\scope1");
        Assert.AreEqual(1, coordinator.Pending);

        // Second request should be dropped.
        coordinator.RequestReplay("Request2", "C:\\scope2");
        Assert.AreEqual(1, coordinator.Pending, "Second request should be dropped");

        // Many more requests should also be dropped without exception.
        for (var i = 0; i < 100; i++)
        {
            coordinator.RequestReplay($"Request{i}", $"C:\\scope{i}");
        }

        Assert.AreEqual(1, coordinator.Pending, "Pending count should remain 1");
    }
}
