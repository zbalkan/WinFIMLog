using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.FIM;
using WinFIMLog.Jobs;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FileSystemRenameNormalizationTests
{
    [TestMethod]
    public void Complete_runtime_pair_remains_a_qualified_rename()
    {
        var notification = FileSystemMonitorJob.NormalizeRename("C:\\", "C:\\old.txt",
            "C:\\new.txt", DateTimeOffset.UtcNow);

        Assert.IsNotNull(notification);
        var value = notification.Value;
        Assert.AreEqual(ChangeCategory.Changed, value.Category);
        Assert.AreEqual("C:\\old.txt", value.OldPath);
        Assert.AreEqual("C:\\new.txt", value.NewPath);
    }

    [TestMethod]
    public void Unmatched_old_name_is_a_removal_without_an_invented_destination()
    {
        var notification = FileSystemMonitorJob.NormalizeRename("C:\\", "C:\\old.txt", null,
            DateTimeOffset.UtcNow);

        Assert.IsNotNull(notification);
        var value = notification.Value;
        Assert.AreEqual(ChangeCategory.Deleted, value.Category);
        Assert.AreEqual("C:\\old.txt", value.FullPath);
        Assert.IsNull(value.NewPath);
    }

    [TestMethod]
    public void Unmatched_new_name_is_an_addition_without_an_invented_source()
    {
        var notification = FileSystemMonitorJob.NormalizeRename("C:\\", null, "C:\\new.txt",
            DateTimeOffset.UtcNow);

        Assert.IsNotNull(notification);
        var value = notification.Value;
        Assert.AreEqual(ChangeCategory.Created, value.Category);
        Assert.AreEqual("C:\\new.txt", value.FullPath);
        Assert.IsNull(value.OldPath);
    }

    [TestMethod]
    public void Move_out_of_scope_is_a_deletion_and_does_not_disclose_the_destination()
    {
        var notification = FileSystemMonitorJob.NormalizeRenameForScope("C:\\scope",
            "C:\\scope\\old.txt", "C:\\outside\\new.txt",
            path => path.StartsWith("C:\\scope", StringComparison.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);

        Assert.IsNotNull(notification);
        var value = notification.Value;
        Assert.AreEqual(ChangeCategory.Deleted, value.Category);
        Assert.AreEqual("C:\\scope\\old.txt", value.FullPath);
        Assert.IsNull(value.NewPath);
    }

    [TestMethod]
    public void Move_into_scope_is_a_creation_and_does_not_disclose_the_source()
    {
        var notification = FileSystemMonitorJob.NormalizeRenameForScope("C:\\scope",
            "C:\\outside\\old.txt", "C:\\scope\\new.txt",
            path => path.StartsWith("C:\\scope", StringComparison.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);

        Assert.IsNotNull(notification);
        var value = notification.Value;
        Assert.AreEqual(ChangeCategory.Created, value.Category);
        Assert.AreEqual("C:\\scope\\new.txt", value.FullPath);
        Assert.IsNull(value.OldPath);
    }
}
