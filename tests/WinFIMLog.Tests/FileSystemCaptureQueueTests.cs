using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;
using WinFIMLog.Health;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemCaptureQueueTests
{
    [TestMethod]
    public async Task Completion_drains_every_admitted_item_before_reader_finishes()
    {
        var metrics = new HealthMetrics();
        var queue = new FileSystemCaptureQueue(4, metrics, new RecordingReporter());
        Assert.IsTrue(queue.TryAdmit(new("C:\\", "C:\\one", ChangeCategory.Created, DateTimeOffset.UtcNow)));
        Assert.IsTrue(queue.TryAdmit(new("C:\\", "C:\\two", ChangeCategory.Changed, DateTimeOffset.UtcNow)));
        queue.CompleteWriter();

        var drained = new List<RawFileSystemNotification>();
        await foreach (var item in queue.ReadAllAsync())
        {
            drained.Add(item);
            queue.Complete(succeeded: true);
        }

        Assert.HasCount(2, drained);
        Assert.AreEqual(0, metrics.QueueDepth);
        Assert.AreEqual(2, metrics.Processed);
        Assert.IsFalse(queue.TryAdmit(new("C:\\", "C:\\late", ChangeCategory.Deleted, DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public async Task FailedEnrichmentIsCountedSeparately()
    {
        var metrics = new HealthMetrics();
        var queue = new FileSystemCaptureQueue(1, metrics, new RecordingReporter());
        queue.TryAdmit(new("C:\\", "C:\\one", ChangeCategory.Created, DateTimeOffset.UtcNow));
        _ = await queue.ReadAsync(CancellationToken.None);
        queue.Complete(succeeded: false);
        Assert.AreEqual(1, metrics.EnrichmentFailures);
        Assert.AreEqual(0, metrics.Processed);
    }

    [TestMethod]
    public async Task SaturationIsBoundedAndReportedAsCoverageGap()
    {
        var metrics = new HealthMetrics();
        var reporter = new RecordingReporter();
        var queue = new FileSystemCaptureQueue(1, metrics, reporter);
        var first = new RawFileSystemNotification("C:\\", "C:\\one", ChangeCategory.Changed, DateTimeOffset.UtcNow);
        var second = new RawFileSystemNotification("C:\\", "C:\\two", ChangeCategory.Changed, DateTimeOffset.UtcNow);

        Assert.IsTrue(queue.TryAdmit(first));
        Assert.IsFalse(queue.TryAdmit(second));
        Assert.AreEqual(1, metrics.QueueDepth);
        Assert.AreEqual(1, metrics.Accepted);
        Assert.AreEqual(1, metrics.Dropped);
        Assert.AreEqual("CaptureQueueFull", reporter.Reason);

        Assert.AreEqual(first, await queue.ReadAsync(CancellationToken.None));
        queue.Complete(succeeded: true);
        Assert.AreEqual(0, metrics.QueueDepth);
        Assert.AreEqual(1, metrics.Processed);
    }

    private sealed class RecordingReporter : IHealthReporter
    {
        public string? Reason { get; private set; }

        public void CoverageGap(string source, string scope, string reason, long lostCount = 1) => Reason = reason;

        public void SinkFailure(string sink, string reason, int attempt)
        { }

        public void SourceRecovered(string source, string scope, string action)
        { }
    }
}
