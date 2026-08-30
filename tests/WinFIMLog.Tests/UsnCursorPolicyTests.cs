using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;
using WinFIMLog.USN;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnCursorPolicyTests
{
    private const ulong JournalId = 0x5150;

    [TestMethod]
    public void Absent_cursor_reads_retained_history_once_without_reporting_a_gap()
    {
        var decision = UsnCursorPolicy.Decide(null, JournalId,
            firstUsn: 100, lowestValidUsn: 100, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.FirstRun, decision.Action);
        Assert.AreEqual(100, decision.StartUsn);
        Assert.IsFalse(decision.IsGap, "A first run loses nothing that was ever observable to this service.");
    }

    [TestMethod]
    public void Cursor_inside_the_retained_range_resumes_where_it_stopped()
    {
        var cursor = Cursor(JournalId, lastReadUsn: 500);

        var decision = UsnCursorPolicy.Decide(cursor, JournalId,
            firstUsn: 100, lowestValidUsn: 100, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.Resume, decision.Action);
        Assert.AreEqual(500, decision.StartUsn);
        Assert.IsFalse(decision.IsGap);
    }

    [TestMethod]
    public void Recreated_journal_is_a_gap_and_restarts_at_the_retained_floor()
    {
        var cursor = Cursor(journalId: 0x1111, lastReadUsn: 500);

        var decision = UsnCursorPolicy.Decide(cursor, JournalId,
            firstUsn: 100, lowestValidUsn: 100, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.JournalWrap, decision.Action);
        Assert.AreEqual(100, decision.StartUsn);
        Assert.IsTrue(decision.IsGap);
        Assert.AreEqual("JournalWrap", decision.Reason);
    }

    [TestMethod]
    public void Cursor_trimmed_out_of_the_ring_is_a_gap_and_restarts_at_the_floor()
    {
        var cursor = Cursor(JournalId, lastReadUsn: 50);

        var decision = UsnCursorPolicy.Decide(cursor, JournalId,
            firstUsn: 100, lowestValidUsn: 400, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.CursorAgeOut, decision.Action);
        Assert.AreEqual(400, decision.StartUsn);
        Assert.IsTrue(decision.IsGap);
    }

    [TestMethod]
    public void Retained_floor_is_the_higher_of_first_and_lowest_valid()
    {
        // After trimming, FirstUsn can lag LowestValidUsn; starting at FirstUsn would ask the driver
        // for records it has already discarded.
        var decision = UsnCursorPolicy.Decide(null, JournalId,
            firstUsn: 100, lowestValidUsn: 700, nextUsn: 900);

        Assert.AreEqual(700, decision.StartUsn);
    }

    [TestMethod]
    public void Cursor_ahead_of_the_journal_head_re_reads_rather_than_skipping()
    {
        var cursor = Cursor(JournalId, lastReadUsn: 5_000);

        var decision = UsnCursorPolicy.Decide(cursor, JournalId,
            firstUsn: 100, lowestValidUsn: 100, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.CursorAgeOut, decision.Action);
        Assert.AreEqual(100, decision.StartUsn);
    }

    [TestMethod]
    public void Invalidated_cursor_is_treated_as_absent()
    {
        var cursor = Cursor(JournalId, lastReadUsn: 500);
        cursor.IsValid = false;

        var decision = UsnCursorPolicy.Decide(cursor, JournalId,
            firstUsn: 100, lowestValidUsn: 100, nextUsn: 900);

        Assert.AreEqual(UsnCursorAction.FirstRun, decision.Action);
    }

    private static UsnJournalCursor Cursor(ulong journalId, long lastReadUsn) => new()
    {
        JournalId = journalId,
        LastReadUsn = lastReadUsn,
        VolumeKey = "DEADBEEF|C",
        IsValid = true
    };
}
