using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alphaleonis.Win32.Vss;
using Microsoft.Extensions.Logging;

namespace WinFIMLog.Snapshots
{
    public interface IFileSystemSnapshotProvider { Task<IFileSystemSnapshot> CreateAsync(VssDriveGroup driveGroup, CancellationToken cancellationToken = default); }
    public interface IFileSystemSnapshot : IDisposable
    {
        string SnapshotSetId { get; }
        Task CompleteAsync(CancellationToken cancellationToken = default);
        string GetVolumeDevicePath(string sourceVolumeRoot);
        string ToCapturePath(string livePath);
        string ToEvidencePath(string capturePath);
    }
    public sealed class AlphaVssFileSystemSnapshotProvider(ILogger<AlphaVssFileSystemSnapshotProvider> logger) : IFileSystemSnapshotProvider
    {
        private static readonly EventId CreationStarted = new(7804, "VssSnapshotCreationStarted");
        private static readonly EventId SnapshotReady = new(7805, "VssSnapshotReady");
        private static readonly EventId CreationFailed = new(7806, "VssSnapshotCreationFailed");
        public async Task<IFileSystemSnapshot> CreateAsync(VssDriveGroup driveGroup, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(driveGroup);
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("VSS filesystem snapshots require Windows.");
            }

            logger.LogInformation(CreationStarted, "VSS inventory snapshot creation started. SourceVolumeRoot={SourceVolumeRoot} TargetCount={TargetCount}", driveGroup.SourceVolumeRoot, driveGroup.MonitoredRoots.Count);
            cancellationToken.ThrowIfCancellationRequested(); IVssBackupComponents? backup = null; var setId = Guid.Empty;
            try
            {
                backup = VssFactoryProvider.Default.GetVssFactory().CreateVssBackupComponents();
                backup.InitializeForBackup(null!); backup.SetContext(VssSnapshotContext.Backup); backup.SetBackupState(false, false, VssBackupType.Full, false);
                await backup.GatherWriterMetadataAsync(cancellationToken).ConfigureAwait(false); backup.FreeWriterMetadata(); setId = backup.StartSnapshotSet();
                if (!backup.IsVolumeSupported(driveGroup.SourceVolumeRoot))
                {
                    throw new NotSupportedException($"No VSS provider supports monitored volume '{driveGroup.SourceVolumeRoot}'.");
                }

                var snapshotId = backup.AddToSnapshotSet(driveGroup.SourceVolumeRoot);
                await backup.PrepareForBackupAsync(cancellationToken).ConfigureAwait(false); await backup.DoSnapshotSetAsync(cancellationToken).ConfigureAwait(false);
                var root = backup.GetSnapshotProperties(snapshotId).SnapshotDeviceObject;
                var result = new AlphaVssFileSystemSnapshot(backup, setId, new SnapshotPathMap(new Dictionary<string, string> { [driveGroup.SourceVolumeRoot] = root }), logger); backup = null;
                logger.LogInformation(SnapshotReady, "VSS inventory snapshot set {SnapshotSetId} is ready. SourceVolumeRoot={SourceVolumeRoot} SnapshotDeviceObject={SnapshotDeviceObject} TargetCount={TargetCount}", setId, driveGroup.SourceVolumeRoot, root, driveGroup.MonitoredRoots.Count);
                return result;
            }
            catch (Exception exception)
            {
                logger.LogError(CreationFailed, exception, "VSS inventory snapshot creation failed. SourceVolumeRoot={SourceVolumeRoot} SnapshotSetId={SnapshotSetId}", driveGroup.SourceVolumeRoot, setId);
                if (backup is not null) { if (setId != Guid.Empty) { try { _ = backup.DeleteSnapshotSet(setId, true); } catch (Exception cleanup) { logger.LogError(cleanup, "VSS inventory could not delete failed snapshot set {SnapshotSetId}.", setId); } } backup.Dispose(); }
                throw;
            }
        }
    }
    internal sealed class AlphaVssFileSystemSnapshot(IVssBackupComponents backup, Guid snapshotSetId, SnapshotPathMap paths, ILogger logger) : IFileSystemSnapshot
    {
        private static readonly EventId BackupStarted = new(7807, "VssBackupCompleteStarted"), Acknowledged = new(7808, "VssInventoryCompletionAcknowledged"), DeleteStarted = new(7809, "VssSnapshotDeletionStarted"), DeleteCompleted = new(7810, "VssSnapshotDeletionCompleted"), DeleteFailed = new(7811, "VssSnapshotDeletionFailed");
        private IVssBackupComponents? backup = backup; private int completed;
        public string SnapshotSetId { get; } = snapshotSetId.ToString("D");
        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            var previous = Interlocked.CompareExchange(ref completed, 1, 0); if (previous == 2)
            {
                return;
            }

            if (previous == 1)
            {
                throw new InvalidOperationException("VSS BackupComplete is already in progress.");
            }

            try { var value = Volatile.Read(ref backup) ?? throw new ObjectDisposedException(nameof(AlphaVssFileSystemSnapshot)); logger.LogInformation(BackupStarted, "VSS BackupComplete started for snapshot set {SnapshotSetId}.", SnapshotSetId); await value.BackupCompleteAsync(cancellationToken).ConfigureAwait(false); Volatile.Write(ref completed, 2); logger.LogInformation(Acknowledged, "VSS inventory completion acknowledged for snapshot set {SnapshotSetId}.", SnapshotSetId); }
            catch { Volatile.Write(ref completed, 0); throw; }
        }
        public string GetVolumeDevicePath(string root) => paths.GetVolumeDevicePath(root);
        public string ToCapturePath(string path) => paths.ToCapturePath(path);
        public string ToEvidencePath(string path) => paths.ToEvidencePath(path);
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref backup, null); if (value is null)
            {
                return;
            }

            try { logger.LogInformation(DeleteStarted, "VSS snapshot-set deletion started. SnapshotSetId={SnapshotSetId}", SnapshotSetId); _ = value.DeleteSnapshotSet(Guid.Parse(SnapshotSetId), true); logger.LogInformation(DeleteCompleted, "VSS snapshot-set deletion completed. SnapshotSetId={SnapshotSetId}", SnapshotSetId); }
            catch (Exception exception) { logger.LogError(DeleteFailed, exception, "VSS snapshot-set deletion failed. SnapshotSetId={SnapshotSetId}", SnapshotSetId); }
            finally { value.Dispose(); }
        }
    }
}
