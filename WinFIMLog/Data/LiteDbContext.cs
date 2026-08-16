using System;
using WinFIMLog.FIM;
using LiteDB;
using Microsoft.Extensions.Options;

namespace WinFIMLog.Data
{
    public partial class LiteDbContext : ILiteDbContext
    {
        public ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        public ILiteCollection<RegistryChange> RegistryChanges { get; }

        /// <summary>
        ///     The default size is 80MB
        /// </summary>
        private const long InitialDatabaseSize = 80 * MB;

        private const long MB = 1024 * 1024;

        private readonly LiteDatabase _database;

        private bool disposedValue;

        /// <summary>
        ///     Hardcoded database file name is fim.db. Initial database size is set to 800MB for
        ///     performance reasons.
        /// </summary>
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
        }

        #region Dispose

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
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

        #endregion Dispose
    }
}