using System;
using System.Collections.Generic;
using System.Linq;

namespace WinFIMLog.Snapshots
{
    public sealed class SnapshotUnstableException(string message) : Exception(message)
    { }

    internal static class CursorlessSnapshotConvergence
    {
        internal static (IReadOnlyList<BaselineMember> Members, int Passes) Capture(
            Func<IReadOnlyList<BaselineMember>> capture, int maximumPasses = 3)
        {
            if (maximumPasses < 2) throw new ArgumentOutOfRangeException(nameof(maximumPasses));
            var previous = capture();
            for (var pass = 2; pass <= maximumPasses; pass++)
            {
                var current = capture();
                if (Equivalent(previous, current)) return (current, pass);
                previous = current;
            }
            throw new SnapshotUnstableException(
                $"Filesystem observations did not converge within {maximumPasses} passes.");
        }

        private static bool Equivalent(IReadOnlyList<BaselineMember> left, IReadOnlyList<BaselineMember> right)
        {
            if (left.Count != right.Count) return false;
            var before = left.ToDictionary(x => x.Identity, x => x.Fingerprint, StringComparer.OrdinalIgnoreCase);
            return right.All(item => before.TryGetValue(item.Identity, out var fingerprint) &&
                string.Equals(fingerprint, item.Fingerprint, StringComparison.Ordinal));
        }
    }
}
