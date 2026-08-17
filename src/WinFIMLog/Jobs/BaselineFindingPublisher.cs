using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinFIMLog.Events;
using WinFIMLog.IO;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Jobs
{
    internal sealed class BaselineFindingPublisher(
        BaselineRepository repository,
        ILocalEventSink eventSink,
        ILogger<BaselineFindingPublisher> logger) : BackgroundService
    {
        internal bool PublishPending()
        {
            var worked = false;
            foreach (var result in repository.PendingResults())
            {
                var baseline = repository.Find(result.BaselineId);
                if (baseline is null)
                {
                    continue;
                }

                worked = true;
                try
                {
                    eventSink.Write(EventContract.Create(7795, "BaselineFinding", result.Id,
                        baseline.ScopeHash, new Dictionary<string, object?>
                        {
                            ["baselineId"] = baseline.Id,
                            ["source"] = baseline.Source.ToString(),
                            ["change"] = result.Change.ToString(),
                            ["identity"] = result.Identity,
                            ["oldPath"] = result.OldPath,
                            ["newPath"] = result.NewPath,
                            ["detectedAt"] = result.DetectedAt
                        }, EventChannel.Baseline));
                    repository.RecordDeliveryAttempt(result, true);
                }
                catch (Exception exception)
                {
                    repository.RecordDeliveryAttempt(result, false);
                    logger.LogError(exception, "Baseline finding {FindingId} remains pending", result.Id);
                }
            }
            return worked;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!PublishPending())
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                }
            }
        }
    }
}
