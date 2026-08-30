using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Jobs;
using WinFIMLog.USN;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnChangeMapperTests
{
    [TestMethod]
    public void Parent_and_filename_are_joined_with_exactly_one_separator()
    {
        Assert.AreEqual(@"C:\Windows\System32\hosts",
            UsnChangeMapper.CombinePath(@"C:\Windows\System32", "hosts"));
        Assert.AreEqual(@"C:\hosts", UsnChangeMapper.CombinePath(@"C:\", "hosts"));
        Assert.AreEqual(@"C:\Windows", UsnChangeMapper.CombinePath(@"C:\Windows", string.Empty));
    }

    [TestMethod]
    public void Placeholder_parent_is_recognised_as_unresolved()
    {
        // DirectoryPathCache returns "<drive>:\?" when OpenFileById cannot reach the parent, which
        // happens precisely when the parent directory was itself deleted.
        Assert.IsTrue(UsnChangeMapper.IsUnresolved(@"C:\?"));
        Assert.IsTrue(UsnChangeMapper.IsUnresolved(string.Empty));
        Assert.IsFalse(UsnChangeMapper.IsUnresolved(@"C:\Program Files\App"));
    }

    [TestMethod]
    public void No_rooted_monitored_path_means_no_volume_is_replayed()
    {
        var configuration = new EffectiveSettings { MonitoredPaths = ["", "relative\\path"] };

        Assert.AreEqual(0, FileSystemUsnJournalReplayWorker.MonitoredVolumes(configuration).Count);
    }

    [TestMethod]
    public void Volume_key_is_case_normalised_so_a_cursor_is_not_duplicated()
    {
        Assert.AreEqual(UsnJournalCursorRepository.VolumeKey("DEADBEEF", 'c'),
            UsnJournalCursorRepository.VolumeKey("DEADBEEF", 'C'));
    }

    [TestMethod]
    public void A_replay_request_storm_coalesces_to_one_pending_replay()
    {
        // A single replay reads from the cursor to the journal head, so a burst of gap reports
        // describes one window. Queueing each would re-read the same span repeatedly.
        var coordinator = new UsnReplayCoordinator();

        for (var index = 0; index < 10_000; index++)
        {
            coordinator.RequestReplay($"overflow-{index}", @"C:\scope");
        }

        Assert.AreEqual(1, coordinator.Pending);
    }
}
