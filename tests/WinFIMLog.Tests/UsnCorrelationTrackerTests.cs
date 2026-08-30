using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;
using WinFIMLog.USN;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnCorrelationTrackerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private const string Path = @"C:\Program Files\App\config.json";

    [TestMethod]
    public void Record_already_reported_by_the_watcher_is_suppressed()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path, ChangeCategory.Changed, Noon);

        Assert.IsFalse(tracker.ShouldPublish(Path, ChangeCategory.Changed, Noon.AddSeconds(3)));
        Assert.AreEqual(1, tracker.SuppressedUsnRecords);
        Assert.AreEqual(0, tracker.AdmittedUsnRecords);
    }

    [TestMethod]
    public void Record_the_watcher_never_reported_is_published()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));

        Assert.IsTrue(tracker.ShouldPublish(Path, ChangeCategory.Deleted, Noon));
        Assert.AreEqual(1, tracker.AdmittedUsnRecords);
    }

    [TestMethod]
    public void Matching_is_case_insensitive_because_paths_are()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path.ToUpperInvariant(), ChangeCategory.Changed, Noon);

        Assert.IsFalse(tracker.ShouldPublish(Path.ToLowerInvariant(), ChangeCategory.Changed, Noon));
    }

    [TestMethod]
    public void A_different_category_on_the_same_path_is_a_separate_observation()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path, ChangeCategory.Created, Noon);

        // The create was seen by Tier 1; a later delete of the same path was not.
        Assert.IsTrue(tracker.ShouldPublish(Path, ChangeCategory.Deleted, Noon.AddSeconds(2)));
    }

    [TestMethod]
    public void Observation_older_than_the_window_no_longer_suppresses()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path, ChangeCategory.Changed, Noon);

        Assert.IsTrue(tracker.ShouldPublish(Path, ChangeCategory.Changed, Noon.AddSeconds(31)));
    }

    [TestMethod]
    public void Suppression_works_when_the_journal_timestamp_precedes_the_watcher_capture()
    {
        // Journal timestamps and watcher capture times come from different clocks for the same
        // operation, so their ordering is not guaranteed and matching must be symmetric.
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path, ChangeCategory.Changed, Noon.AddSeconds(5));

        Assert.IsFalse(tracker.ShouldPublish(Path, ChangeCategory.Changed, Noon));
    }

    [TestMethod]
    public void Pruning_drops_only_observations_past_the_window()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(@"C:\old.txt", ChangeCategory.Changed, Noon);
        tracker.RecordWatcherObservation(@"C:\recent.txt", ChangeCategory.Changed, Noon.AddSeconds(25));

        tracker.Prune(Noon.AddSeconds(40));

        Assert.AreEqual(1, tracker.Count);
        Assert.IsTrue(tracker.ShouldPublish(@"C:\old.txt", ChangeCategory.Changed, Noon.AddSeconds(40)));
        Assert.IsFalse(tracker.ShouldPublish(@"C:\recent.txt", ChangeCategory.Changed, Noon.AddSeconds(30)));
    }

    [TestMethod]
    public void Repeated_observation_extends_the_window_instead_of_duplicating()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));
        tracker.RecordWatcherObservation(Path, ChangeCategory.Changed, Noon);
        tracker.RecordWatcherObservation(Path, ChangeCategory.Changed, Noon.AddSeconds(20));

        Assert.AreEqual(1, tracker.Count);
        Assert.IsFalse(tracker.ShouldPublish(Path, ChangeCategory.Changed, Noon.AddSeconds(45)));
    }

    [TestMethod]
    public void Empty_path_publishes_rather_than_silently_dropping()
    {
        var tracker = new UsnCorrelationTracker(TimeSpan.FromSeconds(30));

        // An unresolved record still carries evidence that something happened; failing open keeps it.
        Assert.IsTrue(tracker.ShouldPublish(string.Empty, ChangeCategory.Deleted, Noon));
    }
}
