using System;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.FIM;
using WinFIMLog.Health;
using Xunit;

namespace WinFIMLog.Tests;

public sealed class FileSystemCaptureQueueTests
{
    [Fact]
    public async Task SaturationIsBoundedAndReportedAsCoverageGap()
    {
        var metrics = new HealthMetrics();
        var reporter = new RecordingReporter();
        var queue = new FileSystemCaptureQueue(1, metrics, reporter);
        var first = new RawFileSystemNotification("C:\\", "C:\\one", ChangeCategory.Changed, DateTimeOffset.UtcNow);
        var second = new RawFileSystemNotification("C:\\", "C:\\two", ChangeCategory.Changed, DateTimeOffset.UtcNow);

        Assert.True(queue.TryAdmit(first));
        Assert.False(queue.TryAdmit(second));
        Assert.Equal(1, metrics.QueueDepth);
        Assert.Equal(1, metrics.Accepted);
        Assert.Equal(1, metrics.Dropped);
        Assert.Equal("CaptureQueueFull", reporter.Reason);

        Assert.Equal(first, await queue.ReadAsync(CancellationToken.None));
        queue.Complete(succeeded: true);
        Assert.Equal(0, metrics.QueueDepth);
        Assert.Equal(1, metrics.Processed);
    }

    [Fact]
    public async Task FailedEnrichmentIsCountedSeparately()
    {
        var metrics = new HealthMetrics();
        var queue = new FileSystemCaptureQueue(1, metrics, new RecordingReporter());
        queue.TryAdmit(new("C:\\", "C:\\one", ChangeCategory.Created, DateTimeOffset.UtcNow));
        _ = await queue.ReadAsync(CancellationToken.None);
        queue.Complete(succeeded: false);
        Assert.Equal(1, metrics.EnrichmentFailures);
        Assert.Equal(0, metrics.Processed);
    }

    private sealed class RecordingReporter : IHealthReporter
    {
        public string? Reason { get; private set; }
        public void CoverageGap(string source, string scope, string reason, long lostCount = 1) => Reason = reason;
        public void SourceRecovered(string source, string scope, string action) { }
        public void SinkFailure(string sink, string reason, int attempt) { }
    }
}
