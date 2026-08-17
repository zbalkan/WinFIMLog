namespace WinFIMLog.Snapshots
{
    public interface ISnapshotCoordinator
    {
        void RequestFileSystemSnapshot(string reason, string? affectedScope = null);

        void RequestRegistrySnapshot(string reason, string? affectedScope = null);

        void RequestScopeSnapshot(string reason);
    }
}
