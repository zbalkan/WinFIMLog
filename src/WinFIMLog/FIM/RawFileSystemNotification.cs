using System;

namespace WinFIMLog.FIM
{
    /// <summary>
    /// The deliberately minimal record copied by a FileSystemWatcher callback.
    /// </summary>
    public readonly record struct RawFileSystemNotification(string Scope, string FullPath,
        ChangeCategory Category, DateTimeOffset CapturedAt, string? OldPath = null, string? NewPath = null);
}
