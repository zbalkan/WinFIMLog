using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class RawFileSystemNotificationTests
{
    [TestMethod]
    public void Rename_payload_preserves_old_and_new_paths()
    {
        var notification = new RawFileSystemNotification("scope", @"C:\new.txt",
            ChangeCategory.Changed, DateTimeOffset.UtcNow, @"C:\old.txt", @"C:\new.txt");
        Assert.AreEqual(@"C:\old.txt", notification.OldPath);
        Assert.AreEqual(@"C:\new.txt", notification.NewPath);
    }
}
