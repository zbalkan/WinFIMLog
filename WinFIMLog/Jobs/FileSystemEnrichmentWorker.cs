using System;
using System.Threading;
using System.Threading.Tasks;
using FastCache;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Data;
using WinFIMLog.FIM;
using WinFIMLog.Health;

namespace WinFIMLog.Jobs
{
    internal sealed class FileSystemEnrichmentWorker : BackgroundService
    {
        private readonly FileSystemCaptureQueue _capture;
        private readonly ILiteDbContext _context;
        private readonly IBuffer<FileSystemChange> _output;
        private readonly FileSystemEventAttributionMonitor _attribution;
        private readonly Settings _settings;
        private readonly ILogger<FileSystemEnrichmentWorker> _logger;

        public FileSystemEnrichmentWorker(FileSystemCaptureQueue capture, ILiteDbContext context,
            IBuffer<FileSystemChange> output, Settings settings, ILogger<FileSystemEnrichmentWorker> logger)
        {
            _capture = capture;
            _context = context;
            _output = output;
            _settings = settings;
            _logger = logger;
            _attribution = new FileSystemEventAttributionMonitor(logger, settings);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _attribution.Start();
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var raw = await _capture.ReadAsync(stoppingToken);
                var succeeded = false;
                try
                {
                    await EnrichAsync(raw, stoppingToken);
                    succeeded = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not enrich filesystem notification for {Path}", raw.FullPath);
                }
                finally { _capture.Complete(succeeded); }
            }
        }

        private async Task EnrichAsync(RawFileSystemNotification raw, CancellationToken cancellationToken)
        {
            var change = FileSystemChange.FromPath(raw.FullPath, raw.Category, _settings.HashLimitMB);
            if (change == null) return;

            // Correlation waiting, hashing, ACL access and database reads all happen here, never
            // on the native watcher callback thread.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (_attribution.TryGet(raw.FullPath, out var attribution))
                {
                    change.ProcessID = attribution.ProcessID;
                    change.ProcessName = attribution.ProcessName;
                    change.Username = attribution.Username;
                    change.UserSID = attribution.UserSID;
                    change.AttributionStatus = AttributionStatus.Attributed;
                    break;
                }
                await Task.Delay(10, cancellationToken);
            }

            FileSystemChange? previous = null;
            if (_settings.EnableLocalDatabase)
            {
                previous = FileSystemChange.RetrievePreviousChange(raw.FullPath, _context);
                change.PreviousHash = previous?.CurrentHash ?? string.Empty;
            }

            var fingerprint = $"{change.ChangeCategory}\0{change.ObjectType}\0{change.CurrentHash}\0{change.ACLs}";
            var duplicate = Cached<string>.TryGet(change.FullPath, out var cached) && cached == fingerprint;
            var changed = change.ChangeCategory is ChangeCategory.Created or ChangeCategory.Deleted || previous == null ||
                change.ObjectType != previous.ObjectType ||
                !string.Equals(change.CurrentHash, previous.CurrentHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(change.ACLs, previous.ACLs, StringComparison.Ordinal);
            if (changed && !duplicate)
            {
                await _output.Add(change);
                Cached<string>.Save(raw.FullPath, fingerprint, TimeSpan.FromSeconds(5));
            }
        }

        public override void Dispose()
        {
            _attribution.Dispose();
            base.Dispose();
        }
    }
}
