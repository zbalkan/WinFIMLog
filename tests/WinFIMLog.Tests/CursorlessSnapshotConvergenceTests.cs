using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class CursorlessSnapshotConvergenceTests
{
    [TestMethod]
    public void Capture_requires_two_consecutive_equal_observations()
    {
        var observations = new Queue<IReadOnlyList<BaselineMember>>();
        observations.Enqueue([Member("A", "one")]);
        observations.Enqueue([Member("A", "two")]);
        observations.Enqueue([Member("A", "two")]);

        var result = CursorlessSnapshotConvergence.Capture(observations.Dequeue);

        Assert.AreEqual(3, result.Passes);
        Assert.AreEqual("two", result.Members[0].ContentHash);
    }

    [TestMethod]
    public void Capture_rejects_a_scope_that_never_converges()
    {
        var pass = 0;
        Assert.Throws<SnapshotUnstableException>(() => CursorlessSnapshotConvergence.Capture(
            () => [Member("A", (++pass).ToString())]));
    }

    private static BaselineMember Member(string identity, string hash) => new()
    {
        Identity = identity,
        Path = identity,
        NodeType = SnapshotNodeType.File,
        HashState = HashEvidenceState.Hashed,
        ContentHash = hash,
        AclState = EvidenceAvailability.Available
    };
}
