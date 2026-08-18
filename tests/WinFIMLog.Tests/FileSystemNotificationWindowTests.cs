using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemNotificationWindowTests
{
    [TestMethod]
    public void Create_and_changed_burst_remains_one_creation()
    {
        var records = FileSystemNotificationWindow.Normalize([
            Notification(@"C:\new.txt", ChangeCategory.Created),
            Notification(@"C:\new.txt", ChangeCategory.Changed),
            Notification(@"C:\new.txt", ChangeCategory.Changed)]);

        Assert.HasCount(1, records);
        Assert.AreEqual(ChangeCategory.Created, records[0].Category);
    }

    [TestMethod]
    public void Rename_replaces_preceding_old_path_change_and_absorbs_destination_change()
    {
        var rename = new RawFileSystemNotification(@"C:\", @"C:\new.txt", ChangeCategory.Changed,
            DateTimeOffset.UtcNow, @"C:\old.txt", @"C:\new.txt");
        var records = FileSystemNotificationWindow.Normalize([
            Notification(@"C:\old.txt", ChangeCategory.Changed), rename,
            Notification(@"C:\new.txt", ChangeCategory.Changed)]);

        Assert.HasCount(1, records);
        Assert.AreEqual(rename, records[0]);
    }

    [TestMethod]
    public void Create_then_delete_is_not_collapsed_because_both_are_integrity_relevant()
    {
        var records = FileSystemNotificationWindow.Normalize([
            Notification(@"C:\temporary.txt", ChangeCategory.Created),
            Notification(@"C:\temporary.txt", ChangeCategory.Deleted)]);

        Assert.HasCount(2, records);
    }

    private static RawFileSystemNotification Notification(string path, ChangeCategory category) =>
        new(@"C:\", path, category, DateTimeOffset.UtcNow);
}
