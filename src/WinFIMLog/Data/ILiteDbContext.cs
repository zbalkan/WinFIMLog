using System;
using LiteDB;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Data
{
    public interface ILiteDbContext : IDisposable
    {
        ILiteCollection<BaselineMember> BaselineMembers { get; }
        ILiteCollection<BaselineMetadata> Baselines { get; }
        ILiteCollection<EventOutboxRecord> EventOutbox { get; }
        ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        ILiteCollection<ReconciliationResult> ReconciliationResults { get; }
        ILiteCollection<RegistryChange> RegistryChanges { get; }

        // USN Journal (Tier 0.5) supplementary collections
        ILiteCollection<UsnJournalCursor> UsnJournalCursors { get; }
        ILiteCollection<UsnJournalGap> UsnJournalGaps { get; }

        bool ExecuteTransaction(Action action);
    }
}
