using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WinFIMLog.USN
{
    /// <summary>Requests a journal replay for a window where Tier 1 coverage was lost.</summary>
    public interface IUsnReplayCoordinator
    {
        void RequestReplay(string reason, string? affectedScope = null);
    }

    /// <summary>Coalescing request channel for Tier 0.5 replays.</summary>
    /// <remarks>
    /// A replay reads from the persisted cursor to the journal head, so a burst of gap reports
    /// describes one window rather than many. The channel is bounded at one and drops writes for
    /// the same reason <c>SnapshotService</c> does: the pending request already covers everything a
    /// later one would ask for.
    ///
    /// Requests are always accepted even when no worker is running, because the source is opt-in;
    /// an undrained request is simply discarded rather than blocking a watcher callback.
    /// </remarks>
    public sealed class UsnReplayCoordinator : IUsnReplayCoordinator
    {
        private readonly Channel<UsnReplayRequest> requests = Channel.CreateBounded<UsnReplayRequest>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        public void RequestReplay(string reason, string? affectedScope = null) =>
            requests.Writer.TryWrite(new UsnReplayRequest(reason, affectedScope));

        internal int Pending => requests.Reader.Count;

        internal ValueTask<UsnReplayRequest> ReadAsync(CancellationToken cancellationToken) =>
            requests.Reader.ReadAsync(cancellationToken);
    }

    internal sealed record UsnReplayRequest(string Reason, string? AffectedScope);
}
