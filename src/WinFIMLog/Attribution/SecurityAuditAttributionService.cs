using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinFIMLog.Events;
using WinFIMLog.Health;
using WinFIMLog.IO;

namespace WinFIMLog.Attribution
{
    /// <summary>
    /// Consumes native 4663/4657 subject evidence for an explicitly enabled SACL tier.
    /// </summary>
    internal sealed class SecurityAuditAttributionService : BackgroundService
    {
        private readonly ILocalEventSink eventSink;
        private readonly IHealthReporter health;
        private readonly ILogger<SecurityAuditAttributionService> logger;
        private readonly SaclAttributionOptions options;
        private readonly IAuditPolicyConformance policy;
        private readonly Settings settings;
        private EventLogWatcher? watcher;

        public SecurityAuditAttributionService(IOptions<SaclAttributionOptions> options,
            IAuditPolicyConformance policy, IHealthReporter health,
            ILogger<SecurityAuditAttributionService> logger, ILocalEventSink eventSink,
            Settings settings)
        {
            this.options = options.Value;
            this.policy = policy;
            this.health = health;
            this.logger = logger;
            this.eventSink = eventSink;
            this.settings = settings;
        }

        public override void Dispose()
        {
            watcher?.EventRecordWritten -= OnRecord;
            watcher?.Dispose();
            base.Dispose();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            options.Validate();
            if (!options.Enabled)
            {
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            RequirePolicy(WindowsAuditPolicyConformance.FileSystemSubcategory, "File System");
            RequirePolicy(WindowsAuditPolicyConformance.RegistrySubcategory, "Registry");
            try
            {
                var query = new EventLogQuery("Security", PathType.LogName,
                    "*[System[(EventID=4663 or EventID=4657)]]");
                watcher = new EventLogWatcher(query);
                watcher.EventRecordWritten += OnRecord;
                watcher.Enabled = true;
            }
            catch (Exception exception)
            {
                health.CoverageGap("SACLAttribution", "Security", $"SecurityChannelUnavailable:{exception.GetType().Name}");
                throw new InvalidOperationException("SACL attribution cannot read the Security channel.", exception);
            }
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);

        private bool IsDeclaredScope(string xml)
        {
            var document = XDocument.Parse(xml);
            var eventData = document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "Data", StringComparison.OrdinalIgnoreCase));
            var objectName = eventData.FirstOrDefault(static element =>
                (string?)element.Attribute("Name") is "ObjectName" or "KeyName")?.Value;
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return options.FileScopes.Concat(options.RegistryScopes).Any(scope =>
                objectName.StartsWith(scope, StringComparison.OrdinalIgnoreCase));
        }

        private void OnRecord(object? sender, EventRecordWrittenEventArgs args)
        {
            if (args.EventException is not null)
            {
                health.CoverageGap("SACLAttribution", "Security", $"ReadFailure:{args.EventException.GetType().Name}");
                return;
            }
            using var record = args.EventRecord;
            // Preserve native XML: it contains SubjectUserSid/Name and, for 4657, old/new values.
            var xml = record?.ToXml();
            if (xml is null || !IsDeclaredScope(xml))
            {
                return;
            }

            var document = XDocument.Parse(xml);
            var data = document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "Data", StringComparison.OrdinalIgnoreCase) && element.Attribute("Name") is not null)
                .GroupBy(static element => element.Attribute("Name")!.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Last().Value as object,
                    StringComparer.OrdinalIgnoreCase);
            try
            {
                eventSink.Write(EventContract.Create(7797, "SecurityAuditAttribution",
                    record?.RecordId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString("N"), settings.ScopeHash,
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["nativeEventId"] = record?.Id,
                        ["provider"] = record?.ProviderName,
                        ["subjectUserSid"] = data.GetValueOrDefault("SubjectUserSid"),
                        ["subjectUserName"] = data.GetValueOrDefault("SubjectUserName"),
                        ["objectName"] = data.GetValueOrDefault("ObjectName") ?? data.GetValueOrDefault("KeyName"),
                        ["oldValue"] = data.GetValueOrDefault("OldValue"),
                        ["newValue"] = data.GetValueOrDefault("NewValue"),
                        ["nativeEvidence"] = xml,
                    }));
            }
            catch (Exception exception)
            {
                health.CoverageGap("SACLAttribution", "Security", $"EvidenceWriteFailure:{exception.GetType().Name}");
            }
        }

        private void RequirePolicy(Guid subcategory, string name)
        {
            if (policy.IsEnabled(subcategory, out var reason))
            {
                return;
            }

            health.CoverageGap("SACLAttribution", name, $"AuditPolicyMissing:{reason}", 0);
            throw new InvalidOperationException($"SACL attribution requires the '{name}' audit subcategory: {reason}");
        }
    }
}
