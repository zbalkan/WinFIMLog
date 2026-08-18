using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Events;
using WinFIMLog.FIM;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class FindingEventFactoryTests
{
    [TestMethod]
    public void Vanished_created_item_is_retained_with_unknown_evidence()
    {
        var missing = $@"C:\missing-{Guid.NewGuid():N}.txt";

        var change = FileSystemChange.FromPath(missing, ChangeCategory.Created, 1, "scope",
            retainMissing: true);

        Assert.IsNotNull(change);
        Assert.AreEqual(IO.FileSystem.ObjectType.Unknown, change.ObjectType);
        Assert.IsNull(change.CurrentSizeBytes);
    }

    [TestMethod]
    [DataRow(ChangeCategory.Created, 7776, "Created")]
    [DataRow(ChangeCategory.Changed, 7777, "Modified")]
    [DataRow(ChangeCategory.Deleted, 7778, "Deleted")]
    public void File_system_add_modify_and_remove_use_the_allocated_events(
        ChangeCategory category, int eventId, string operation)
    {
        var record = FindingEventFactory.FileSystem(FileChange(category));

        Assert.AreEqual(eventId, record.EventId);
        Assert.AreEqual("FileSystemFinding", record.RecordType);
        Assert.AreEqual(operation, record.Fields["operation"]);
        Assert.AreEqual("new-acl", record.Fields["currentAcl"]);
        Assert.AreEqual("old-acl", record.Fields["previousAcl"]);
    }

    [TestMethod]
    public void Rename_or_move_preserves_both_paths_and_uses_changed_event()
    {
        var change = FileChange(ChangeCategory.Changed);
        change.OldPath = @"C:\scope\old.txt";
        change.NewPath = @"C:\scope\new.txt";

        var record = FindingEventFactory.FileSystem(change);

        Assert.AreEqual((ushort)7777, record.EventId);
        Assert.AreEqual("RenamedOrMoved", record.Fields["operation"]);
        Assert.AreEqual(change.OldPath, record.Fields["oldPath"]);
        Assert.AreEqual(change.NewPath, record.Fields["newPath"]);
        Assert.AreEqual("RuntimeAdjacentBufferPair", record.Fields["renameCorrelationMethod"]);
        Assert.AreEqual("Low", record.Fields["renameCorrelationConfidence"]);
    }

    [TestMethod]
    public void Copy_is_truthfully_reported_as_creation_because_the_watcher_has_no_copy_primitive()
    {
        var copy = FileChange(ChangeCategory.Created);
        copy.Entity = @"C:\scope\copy.txt";

        var record = FindingEventFactory.FileSystem(copy);

        Assert.AreEqual((ushort)7776, record.EventId);
        Assert.AreEqual("Created", record.Fields["operation"]);
        Assert.IsNull(record.Fields["oldPath"]);
    }

    [TestMethod]
    public void Deleted_item_recovers_type_size_hash_and_acl_from_the_projection()
    {
        var previous = FileChange(ChangeCategory.Created);
        previous.ObjectType = IO.FileSystem.ObjectType.File;
        previous.CurrentSizeBytes = 123;
        var deleted = FileChange(ChangeCategory.Deleted);
        deleted.ObjectType = IO.FileSystem.ObjectType.Unknown;
        deleted.CurrentSizeBytes = null;

        Jobs.FileSystemEnrichmentWorker.ApplyPreviousEvidence(deleted, previous);
        var record = FindingEventFactory.FileSystem(deleted);

        Assert.AreEqual("File", record.Fields["objectType"]);
        Assert.AreEqual(123L, record.Fields["previousSizeBytes"]);
        Assert.AreEqual(previous.CurrentHash, record.Fields["previousHash"]);
        Assert.AreEqual(previous.ACLs, record.Fields["previousAcl"]);
        Assert.IsNull(record.Fields["currentSizeBytes"]);
    }

    [TestMethod]
    [DataRow(ChangeCategory.Created, 7786, "Created")]
    [DataRow(ChangeCategory.Changed, 7787, "Modified")]
    [DataRow(ChangeCategory.Deleted, 7788, "Deleted")]
    public void Registry_add_modify_and_remove_use_the_allocated_events(
        ChangeCategory category, int eventId, string operation)
    {
        var change = new RegistryChange
        {
            Id = Guid.NewGuid().ToString("N"),
            Entity = @"HKEY_CURRENT_USER\Software\Example",
            Hive = "CurrentUser",
            KeyName = "Example",
            ValueName = "Enabled",
            ScopeHash = "scope",
            ChangeCategory = category,
            ACLs = "new-acl",
            PreviousACL = "old-acl"
        };

        var record = FindingEventFactory.Registry(change);

        Assert.AreEqual(eventId, record.EventId);
        Assert.AreEqual("RegistryFinding", record.RecordType);
        Assert.AreEqual(operation, record.Fields["operation"]);
        Assert.AreEqual("new-acl", record.Fields["currentAcl"]);
        Assert.AreEqual("old-acl", record.Fields["previousAcl"]);
    }

    private static FileSystemChange FileChange(ChangeCategory category) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Entity = @"C:\scope\file.txt",
        FullPath = @"C:\scope\file.txt",
        ScopeHash = "scope",
        ChangeCategory = category,
        ACLs = "new-acl",
        PreviousACL = "old-acl",
        CurrentHash = "new-hash",
        PreviousHash = "old-hash"
    };
}
