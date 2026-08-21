using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteDB;
using LiteDB.Generated;
using Microsoft.Extensions.Options;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Data
{
    public partial class LiteDbContext : ILiteDbContext
    {
        private const int LatestStateSchemaVersion = 1;

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
        public LiteDbContext(IOptions<LiteDbOptions> options)
        {
            var mapper = new BsonMapper();
            LiteDbGeneratedMappings.Register(mapper);
            _database = new LiteDatabase(new ConnectionString()
            {
                Filename = options.Value.DatabasePath,
                Connection = ConnectionType.Shared,
                InitialSize = InitialDatabaseSize
            },
            mapper: mapper);

            FileSystemChanges = _database.GetGeneratedCollection<FileSystemChange>("fileSystemChanges");
            FileSystemChanges.EnsureIndex(x => x.Id);
            RegistryChanges = _database.GetGeneratedCollection<RegistryChange>("registryChanges");
            RegistryChanges.EnsureIndex(x => x.Id);
            EnsureLatestStateSchema();

            Baselines = _database.GetGeneratedCollection<BaselineMetadata>("baselines");
            Baselines.EnsureIndex(x => x.Status);
            Baselines.EnsureIndex(x => x.Source);
            BaselineMembers = _database.GetGeneratedCollection<BaselineMember>("baselineMembers");
            BaselineMembers.EnsureIndex(x => x.BaselineId);
            BaselineMembers.EnsureIndex(x => x.Identity);
            ReconciliationResults = _database.GetGeneratedCollection<ReconciliationResult>("reconciliationResults");
            ReconciliationResults.EnsureIndex(x => x.BaselineId);
            ReconciliationResults.EnsureIndex(x => x.DeliveredAt);
            EventOutbox = _database.GetGeneratedCollection<EventOutboxRecord>("eventOutbox");
            EventOutbox.EnsureIndex(x => x.DeliveredAt);
            EventOutbox.EnsureIndex(x => x.NextAttemptAt);
        }

        public ILiteCollection<BaselineMember> BaselineMembers { get; }
        public ILiteCollection<BaselineMetadata> Baselines { get; }
        public ILiteCollection<EventOutboxRecord> EventOutbox { get; }
        public ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        public ILiteCollection<ReconciliationResult> ReconciliationResults { get; }
        public ILiteCollection<RegistryChange> RegistryChanges { get; }

        internal bool LatestStateMigrationPerformed { get; private set; }

        /// <summary>Creates normalized latest-state indexes and migrates legacy rows exactly once.</summary>
        /// <remarks>
        /// The persisted version marker prevents full collection scans on later startups. The
        /// migration of both projections and marker write are atomic; all later openings only
        /// validate the unique indexes used by the indexed per-entity replacement path.
        /// </remarks>
        private void EnsureLatestStateSchema()
        {
            var metadata = _database.GetCollection("databaseMetadata");
            var schema = metadata.FindById("latestState");
            if (schema is null || schema["version"].AsInt32 < LatestStateSchemaVersion)
            {
                if (!ExecuteTransaction(() =>
                    {
                        MigrateLatestState(FileSystemChanges);
                        MigrateLatestState(RegistryChanges);
                        metadata.Upsert(new BsonDocument
                        {
                            ["_id"] = "latestState",
                            ["version"] = LatestStateSchemaVersion
                        });
                    }))
                {
                    throw new InvalidOperationException("Could not migrate latest-state projections.");
                }

                LatestStateMigrationPerformed = true;
            }

            FileSystemChanges.EnsureIndex(x => x.NormalizedEntity, true);
            RegistryChanges.EnsureIndex(x => x.NormalizedEntity, true);
        }

        /// <summary>Normalizes legacy identities and retains the newest case-insensitive duplicate.</summary>
        /// <remarks>
        /// The dictionary makes duplicate resolution expected O(n) while holding only one winner
        /// per identity. This is a versioned migration path, not an ordinary startup operation.
        /// </remarks>
        private static void MigrateLatestState<T>(ILiteCollection<T> collection) where T : Change
        {
            var retained = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var change in collection.FindAll())
            {
                var key = NormalizeEntity(change.Entity);
                if (!retained.TryGetValue(key, out var current) || change.DateTime > current.DateTime)
                {
                    retained[key] = change;
                }
            }

            foreach (var change in collection.FindAll().ToList())
            {
                var key = NormalizeEntity(change.Entity);
                if (!string.Equals(retained[key].Id, change.Id, StringComparison.Ordinal))
                {
                    collection.Delete(change.Id);
                    continue;
                }

                if (!string.Equals(change.NormalizedEntity, key, StringComparison.Ordinal))
                {
                    change.NormalizedEntity = key;
                    collection.Update(change);
                }
            }
        }

        internal static string NormalizeEntity(string entity) => entity.ToUpperInvariant();

        #region Dispose

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public bool ExecuteTransaction(Action action)
        {
            lock (writeLock)
            {
                return _database.BeginTrans() && Execute(action);
            }
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
