using System;
using System.Collections.Generic;

namespace WinFIMLog.FIM
{
    /// <summary>
    /// Collapses the burst of advisory watcher notifications produced by one logical operation.
    /// The window is deliberately bounded; it does not wait for the originating handle to close.
    /// </summary>
    internal static class FileSystemNotificationWindow
    {
        internal static IReadOnlyList<RawFileSystemNotification> Normalize(
            IReadOnlyList<RawFileSystemNotification> notifications)
        {
            var output = new List<RawFileSystemNotification>(notifications.Count);
            foreach (var notification in notifications)
            {
                if (notification.OldPath is not null)
                {
                    // A pre-rename Changed notification describes the same logical operation.
                    output.RemoveAll(item => item.Category == ChangeCategory.Changed &&
                        string.Equals(item.FullPath, notification.OldPath, StringComparison.OrdinalIgnoreCase));
                    output.Add(notification);
                    continue;
                }

                if (notification.Category == ChangeCategory.Changed)
                {
                    var existing = output.FindLastIndex(item =>
                        string.Equals(item.FullPath, notification.FullPath, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0 &&
                        (output[existing].Category is ChangeCategory.Created or ChangeCategory.Changed ||
                         output[existing].OldPath is not null))
                    {
                        // Enrichment runs after the window and observes the final hash and ACL.
                        continue;
                    }
                }

                output.Add(notification);
            }

            return output;
        }
    }
}
