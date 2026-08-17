using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Data;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Health;

namespace WinFIMLog
{
    internal partial class BufferConsumer : BackgroundService
    {
        private const int BUCKET_SIZE = 500;

        private readonly ILiteDbContext _ctx;

        private readonly IBuffer<FileSystemChange> _fsStore;

        private readonly IHealthReporter _health;
        private readonly ILogger<JobOrchestrator> _logger;

        private readonly EventOutboxRepository _outbox;
        private readonly IBuffer<RegistryChange> _regStore;

        private readonly Settings _settings;

        public BufferConsumer(ILogger<JobOrchestrator> logger,
                      IBuffer<FileSystemChange> fsStore,
                      IBuffer<RegistryChange> regStore,
                      ILiteDbContext ctx,
                      Settings settings,
                      IHealthReporter health,
                      EventOutboxRepository outbox)
        {
            _logger = logger;
            _fsStore = fsStore;
            _regStore = regStore;
            _ctx = ctx;
            _settings = settings;
            _health = health;
            _outbox = outbox;
        }

        // Cannot run in parallel as the local database does not support concurrent writes.
        internal bool ProcessChanges() => ProcessFileSystemChanges() | ProcessRegistryChanges();

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

        private static Dictionary<string, T>.ValueCollection LatestByEntity<T>(List<T> changes) where T : IChange
        {
            // GroupBy + OrderBy creates a grouping and a sort buffer for every entity. A single
            // dictionary keeps only one reference per entity and performs no per-group sorting.
            var latest = new Dictionary<string, T>(changes.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                if (!latest.TryGetValue(change.Entity, out var current) || change.DateTime > current.DateTime)
                    latest[change.Entity] = change;
            }

            return latest.Values;
        }

        private bool ProcessFileSystemChanges()
        {
            // read from stores as bulk and write to database.
            var fsCount = Math.Min(_fsStore.Count(), BUCKET_SIZE);
            if (fsCount > 0)
            {
                var fsChanges = _fsStore.Take(fsCount);

                try
                {
                    var records = new List<(EventContract Record, bool Error)>(fsChanges.Count);
                    foreach (var change in fsChanges)
                    {
                        if (change.ChangeCategory != ChangeCategory.Discovery)
                        {
                            var id = change.ChangeCategory switch
                            {
                                ChangeCategory.Created => (ushort)7776,
                                ChangeCategory.Changed => (ushort)7777,
                                ChangeCategory.Deleted => (ushort)7778,
                                _ => (ushort)7780
                            };
                            var record = EventContract.Create(id, "FileSystemFinding", change.Id,
                                change.ScopeHash, new Dictionary<string, object?>
                                {
                                    ["category"] = change.ChangeCategory.ToString(),
                                    ["path"] = change.Entity,
                                    ["oldPath"] = change.OldPath,
                                    ["newPath"] = change.NewPath,
                                    ["currentHash"] = change.CurrentHash,
                                    ["previousHash"] = change.PreviousHash,
                                    ["objectType"] = change.ObjectType.ToString(),
                                    ["attributionStatus"] = change.AttributionStatus.ToString(),
                                    ["attributionMethod"] = change.AttributionMethod,
                                    ["attributionConfidence"] = change.AttributionConfidence,
                                    ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp,
                                    ["attributionMissingReason"] = change.AttributionMissingReason,
                                    ["processSequenceNumber"] = change.ProcessSequenceNumber,
                                    ["processId"] = change.ProcessID,
                                    ["processName"] = change.ProcessName,
                                    ["userSid"] = change.UserSID,
                                    ["username"] = change.Username
                                });
                            records.Add((record, false));
                        }
                    }
                    Retry(() => _outbox.EnqueueBatch(records, () =>
                    {
                        if (_settings.EnableLocalDatabase)
                        {
                            foreach (var change in LatestByEntity(fsChanges))
                            {
                                foreach (var previous in _ctx.FileSystemChanges.FindAll()
                                    .Where(x => string.Equals(x.Entity, change.Entity, StringComparison.OrdinalIgnoreCase)).ToList())
                                    _ctx.FileSystemChanges.Delete(previous.Id);
                                _ctx.FileSystemChanges.Insert(change);
                            }
                        }
                    }), "LiteDBOutbox");
                    Debug.WriteLine($"Successfully persisted and enqueued {fsCount} items.");
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

        private bool ProcessRegistryChanges()
        {
            var regCount = Math.Min(_regStore.Count(), BUCKET_SIZE);
            if (regCount > 0)
            {
                var regChanges = _regStore.Take(regCount);
                try
                {
                    var records = new List<(EventContract Record, bool Error)>(regChanges.Count);
                    foreach (var change in regChanges)
                    {
                        var id = change.ChangeCategory switch
                        {
                            ChangeCategory.Created => (ushort)7786,
                            ChangeCategory.Changed => (ushort)7787,
                            ChangeCategory.Deleted => (ushort)7788,
                            _ => (ushort)7780
                        };
                        var record = EventContract.Create(id, "RegistryFinding", change.Id,
                            change.ScopeHash, new Dictionary<string, object?>
                            {
                                ["category"] = change.ChangeCategory.ToString(),
                                ["key"] = change.Entity,
                                ["hive"] = change.Hive,
                                ["valueName"] = change.ValueName,
                                ["valueData"] = change.ValueData,
                                ["attributionStatus"] = change.AttributionStatus.ToString(),
                                ["attributionMethod"] = change.AttributionMethod,
                                ["attributionConfidence"] = change.AttributionConfidence,
                                ["attributionSourceTimestamp"] = change.AttributionSourceTimestamp,
                                ["attributionMissingReason"] = change.AttributionMissingReason,
                                ["processId"] = change.ProcessID,
                                ["processName"] = change.ProcessName,
                                ["userSid"] = change.UserSID,
                                ["username"] = change.Username
                            });
                        records.Add((record, false));
                    }
                    Retry(() => _outbox.EnqueueBatch(records, () =>
                    {
                        if (_settings.EnableLocalDatabase)
                        {
                            foreach (var change in LatestByEntity(regChanges))
                            {
                                foreach (var previous in _ctx.RegistryChanges.FindAll()
                                    .Where(x => string.Equals(x.Entity, change.Entity, StringComparison.OrdinalIgnoreCase)).ToList())
                                    _ctx.RegistryChanges.Delete(previous.Id);
                                _ctx.RegistryChanges.Insert(change);
                            }
                        }
                    }), "LiteDBOutbox");
                    Debug.WriteLine($"Successfully persisted and enqueued {regCount} items.");
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
    }
}
