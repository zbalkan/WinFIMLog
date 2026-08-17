using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using LiteDB;
using Microsoft.Extensions.Options;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Data
{
    public partial class LiteDbContext : ILiteDbContext
    {
        /// <summary>
        ///     The default size is 80MB
        /// </summary>
        private const long InitialDatabaseSize = 80 * MB;

        private const long MB = 1024 * 1024;
        private readonly LiteDatabase _database;
        private readonly Lock writeLock = new();
        private bool disposedValue;

        /// <summary>
        ///     Hardcoded database file name is fim.db. Initial database size is set to 800MB for
        ///     performance reasons.
        /// </summary>
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor,
            typeof(EventOutboxRecord))]
        public LiteDbContext(IOptions<LiteDbOptions> options)
        {
            _database = new LiteDatabase(new ConnectionString()
            {
                Filename = options.Value.DatabasePath,
                Connection = ConnectionType.Shared,
                InitialSize = InitialDatabaseSize
            });

            FileSystemChanges = _database.GetCollection<FileSystemChange>("fileSystemChanges");
            FileSystemChanges.EnsureIndex(x => x.Id);
            FileSystemChanges.EnsureIndex(x => x.Entity);

            RegistryChanges = _database.GetCollection<RegistryChange>("registryChanges");
            RegistryChanges.EnsureIndex(x => x.Id);
            RegistryChanges.EnsureIndex(x => x.Entity);

            Baselines = _database.GetCollection<BaselineMetadata>("baselines");
            Baselines.EnsureIndex(x => x.Status);
            Baselines.EnsureIndex(x => x.Source);
            BaselineMembers = _database.GetCollection<BaselineMember>("baselineMembers");
            BaselineMembers.EnsureIndex(x => x.BaselineId);
            BaselineMembers.EnsureIndex(x => x.Identity);
            ReconciliationResults = _database.GetCollection<ReconciliationResult>("reconciliationResults");
            ReconciliationResults.EnsureIndex(x => x.BaselineId);
            ReconciliationResults.EnsureIndex(x => x.DeliveredAt);
            EventOutbox = _database.GetCollection<EventOutboxRecord>("eventOutbox");
            EventOutbox.EnsureIndex(x => x.DeliveredAt);
            EventOutbox.EnsureIndex(x => x.NextAttemptAt);
        }

        public ILiteCollection<BaselineMember> BaselineMembers { get; }
        public ILiteCollection<BaselineMetadata> Baselines { get; }
        public ILiteCollection<EventOutboxRecord> EventOutbox { get; }
        public ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        public ILiteCollection<ReconciliationResult> ReconciliationResults { get; }
        public ILiteCollection<RegistryChange> RegistryChanges { get; }

        #region Dispose

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public bool ExecuteTransaction(Action action)
        {
            lock (writeLock) return _database.BeginTrans() && Execute(action);
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _database.Dispose();
                }

                disposedValue = true;
            }
        }

        private bool Execute(Action action)
        {
            try
            {
                action();
                return _database.Commit();
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        #endregion Dispose
    }
}
