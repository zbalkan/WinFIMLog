using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Jobs;
using WinFIMLog.USN;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnChangeMapperTests
{
    [TestMethod]
    public void Parent_and_filename_are_joined_with_a_single_separator()
    {
        Assert.AreEqual(@"C:\Windows\System32\drivers\etc\hosts",
            UsnChangeMapper.CombinePath(@"C:\Windows\System32\drivers\etc", "hosts"));
    }

    [TestMethod]
    public void A_parent_that_already_ends_in_a_separator_does_not_gain_a_second()
    {
        Assert.AreEqual(@"C:\hosts", UsnChangeMapper.CombinePath(@"C:\", "hosts"));
    }

    [TestMethod]
    public void A_record_without_a_filename_keeps_the_parent_path()
    {
        Assert.AreEqual(@"C:\Windows", UsnChangeMapper.CombinePath(@"C:\Windows", string.Empty));
    }

    [TestMethod]
    public void Placeholder_parent_is_recognised_as_unresolved()
    {
        // DirectoryPathCache returns "<drive>:\?" when OpenFileById cannot reach the parent, which
        // happens precisely when the parent directory was itself deleted.
        Assert.IsTrue(UsnChangeMapper.IsUnresolved(@"C:\?"));
        Assert.IsTrue(UsnChangeMapper.IsUnresolved(string.Empty));
    }

    [TestMethod]
    public void A_real_parent_path_is_not_treated_as_unresolved()
    {
        Assert.IsFalse(UsnChangeMapper.IsUnresolved(@"C:\Program Files\App"));
        Assert.IsFalse(UsnChangeMapper.IsUnresolved(@"C:\"));
    }

    [TestMethod]
    public void No_rooted_monitored_path_means_no_volume_is_polled()
    {
        var configuration = new EffectiveSettings { MonitoredPaths = ["", "relative\\path"] };

        Assert.AreEqual(0, FileSystemUsnJournalMonitorJob.MonitoredVolumes(configuration).Count);
    }

    [TestMethod]
    public void Volume_key_is_case_normalised_so_a_cursor_is_not_duplicated()
    {
        Assert.AreEqual(UsnJournalCursorRepository.VolumeKey("DEADBEEF", 'c'),
            UsnJournalCursorRepository.VolumeKey("DEADBEEF", 'C'));
    }
}
