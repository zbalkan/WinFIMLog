// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntegrityService.Data;
using IntegrityService.FIM;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegrityService
{
    internal partial class BufferConsumer : BackgroundService
    {
        private const int BUCKET_SIZE = 500;

        private readonly ILiteDbContext _ctx;

        private readonly IBuffer<FileSystemChange> _fsStore;

        private readonly ILogger<JobOrchestrator> _logger;

        private readonly IBuffer<RegistryChange> _regStore;

        public BufferConsumer(ILogger<JobOrchestrator> logger,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx)
        {
            _logger = logger;
            _fsStore = fsStore;
            _regStore = regStore;
            _ctx = ctx;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Initiated Persistence Worker");
            while (!stoppingToken.IsCancellationRequested)
            {
                // Cannot run in parallel as local database does not support concurrent writes.
                var processedChanges = ProcessFileSystemChanges() | ProcessRegistryChanges();
                if (!processedChanges)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                }
            }
        }

        private bool ProcessFileSystemChanges()
        {
            // read from stores as bulk and write to database.
            var fsCount = Math.Min(_fsStore.Count(), BUCKET_SIZE);
            if (fsCount > 0)
            {
                var fsChanges = _fsStore.Take(fsCount);

                if (Settings.Instance.EnableLocalDatabase)
                {
                    _ = _ctx.FileSystemChanges.InsertBulk(fsChanges.Select(m => m));
                    Debug.WriteLine($"Succesfully inserted {fsCount} items.");
                }

                foreach (var change in fsChanges)
                {
                    if (change.ChangeCategory != ChangeCategory.Discovery)
                    {
                        _logger.LogInformation("Change Type: {changeType:l}\nCategory: {category:l}\nPath: {path:l}\nCurrent Hash: {currentHash:l}\nPreviousHash: {previousHash:l}",
                            Enum.GetName(change.ChangeCategory), Enum.GetName(ConfigChangeType.FileSystem), change.Entity, change.CurrentHash, change.PreviousHash);
                    }
                }

                return true;
            }

            return false;
        }

        private bool ProcessRegistryChanges()
        {
            var regCount = Math.Min(_regStore.Count(), BUCKET_SIZE);
            if (regCount > 0)
            {
                var regChanges = _regStore.Take(regCount);

                if (Settings.Instance.EnableLocalDatabase)
                {
                    _ = _ctx.RegistryChanges.InsertBulk(regChanges.Select(m => m));
                    Debug.WriteLine($"Succesfully inserted {regCount} items.");
                }

                foreach (var change in regChanges)
                {
                    _logger
                        .LogInformation("Change Type: {changeType:l}\nCategory: {category:l}\nEvent Data:\n{ev:l}",
                        Enum.GetName(ConfigChangeType.Registry), Enum.GetName(change.ChangeCategory), change.ToString());
                }

                return true;
            }

            return false;
        }
    }
}
