// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Health;
using WinFIMLog.IO;
using WinFIMLog.Events;
using System.Collections.Generic;

namespace WinFIMLog
{
    internal partial class BufferConsumer : BackgroundService
    {
        private const int BUCKET_SIZE = 500;

        private readonly ILiteDbContext _ctx;

        private readonly IBuffer<FileSystemChange> _fsStore;

        private readonly ILogger<JobOrchestrator> _logger;

        private readonly IBuffer<RegistryChange> _regStore;

        private readonly Settings _settings;
        private readonly IHealthReporter _health;
        private readonly ILocalEventSink _eventSink;

        public BufferConsumer(ILogger<JobOrchestrator> logger,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx,
                      Settings settings,
                      IHealthReporter health,
                      ILocalEventSink eventSink)
        {
            _logger = logger;
            _fsStore = fsStore;
            _regStore = regStore;
            _ctx = ctx;
            _settings = settings;
            _health = health;
            _eventSink = eventSink;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Initiated Persistence Worker");
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!ProcessChanges())
                            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        // ProcessChanges has already returned the batch to its buffer. Keep the
                        // hosted worker alive so a transient disk/database outage can recover.
                        _health.SinkFailure("PersistenceWorker", exception.GetType().Name, 4);
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal service shutdown.
            }
            finally
            {
                // The orchestrator is stopped first, so no new monitor events can arrive while
                // the remaining changes are persisted and logged.
                try
                {
                    while (ProcessChanges()) { }
                }
                catch (Exception exception)
                {
                    _health.CoverageGap("PersistenceWorker", _settings.ScopeHash,
                        $"ShutdownDrainFailed:{exception.GetType().Name}");
                }

                _logger.LogInformation("Persistence worker stopped after draining its buffers");
            }
        }

        // Cannot run in parallel as the local database does not support concurrent writes.
        private bool ProcessChanges() => ProcessFileSystemChanges() | ProcessRegistryChanges();

        private bool ProcessFileSystemChanges()
        {
            // read from stores as bulk and write to database.
            var fsCount = Math.Min(_fsStore.Count(), BUCKET_SIZE);
            if (fsCount > 0)
            {
                var fsChanges = _fsStore.Take(fsCount);

                try
                {
                    if (_settings.EnableLocalDatabase)
                    {
                        Retry(() => _ctx.FileSystemChanges.Upsert(fsChanges), "LiteDB");
                        Debug.WriteLine($"Successfully persisted {fsCount} items.");
                    }

                    foreach (var change in fsChanges)
                    {
                        if (change.ChangeCategory != ChangeCategory.Discovery)
                        {
                            var id = change.ChangeCategory switch
                            { ChangeCategory.Created => (ushort)7776, ChangeCategory.Changed => (ushort)7777,
                              ChangeCategory.Deleted => (ushort)7778, _ => (ushort)7780 };
                            var record = EventContract.Create(id, "FileSystemFinding", change.Id,
                                change.ScopeHash, new Dictionary<string, object?> {
                                    ["category"] = change.ChangeCategory.ToString(), ["path"] = change.Entity,
                                    ["oldPath"] = change.OldPath, ["newPath"] = change.NewPath,
                                    ["currentHash"] = change.CurrentHash, ["previousHash"] = change.PreviousHash,
                                    ["objectType"] = change.ObjectType.ToString(), ["attributionStatus"] = change.AttributionStatus.ToString(),
                                    ["attributionMethod"] = change.AttributionMethod, ["attributionConfidence"] = change.AttributionConfidence,
                                    ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp, ["attributionMissingReason"] = change.AttributionMissingReason,
                                    ["processSequenceNumber"] = change.ProcessSequenceNumber,
                                    ["processId"] = change.ProcessID, ["processName"] = change.ProcessName,
                                    ["userSid"] = change.UserSID, ["username"] = change.Username });
                            Retry(() => _eventSink.Write(record), "EventLog");
                        }
                    }
                }
                catch
                {
                    // Taking a batch is not an acknowledgement: return it on terminal sink
                    // failure so later iterations can retry rather than silently losing it.
                    _fsStore.AddRange(fsChanges).GetAwaiter().GetResult();
                    throw;
                }

                return true;
            }

            return false;
        }

        private void Retry(Func<int> write, string sink)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try { _ = write(); return; }
                catch (Exception exception)
                {
                    last = exception;
                    _health.SinkFailure(sink, exception.GetType().Name, attempt);
                    if (attempt < 3) Thread.Sleep(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))));
                }
            }
            throw new InvalidOperationException($"{sink} write failed after retries; batch was not acknowledged.", last);
        }

        private void Retry(Action write, string sink) => Retry(() => { write(); return 1; }, sink);

        private bool ProcessRegistryChanges()
        {
            var regCount = Math.Min(_regStore.Count(), BUCKET_SIZE);
            if (regCount > 0)
            {
                var regChanges = _regStore.Take(regCount);
                try
                {
                    if (_settings.EnableLocalDatabase)
                    {
                        Retry(() => _ctx.RegistryChanges.Upsert(regChanges), "LiteDB");
                        Debug.WriteLine($"Successfully persisted {regCount} items.");
                    }

                    foreach (var change in regChanges)
                    {
                        var id = change.ChangeCategory switch
                        { ChangeCategory.Created => (ushort)7786, ChangeCategory.Changed => (ushort)7787,
                          ChangeCategory.Deleted => (ushort)7788, _ => (ushort)7780 };
                        var record = EventContract.Create(id, "RegistryFinding", change.Id,
                            change.ScopeHash, new Dictionary<string, object?> {
                                ["category"] = change.ChangeCategory.ToString(), ["key"] = change.Entity,
                                ["hive"] = change.Hive, ["valueName"] = change.ValueName,
                                ["valueData"] = change.ValueData, ["attributionStatus"] = change.AttributionStatus.ToString(),
                                ["attributionMethod"] = change.AttributionMethod, ["attributionConfidence"] = change.AttributionConfidence,
                                ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp, ["attributionMissingReason"] = change.AttributionMissingReason,
                                ["processId"] = change.ProcessID, ["processName"] = change.ProcessName,
                                ["userSid"] = change.UserSID, ["username"] = change.Username });
                        Retry(() => _eventSink.Write(record), "EventLog");
                    }
                }
                catch
                {
                    _regStore.AddRange(regChanges).GetAwaiter().GetResult();
                    throw;
                }

                return true;
            }

            return false;
        }
    }
}
