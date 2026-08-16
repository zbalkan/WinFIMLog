using System;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinFIMLog.Health;

namespace WinFIMLog.Attribution
{
    /// <summary>Consumes native 4663/4657 subject evidence for an explicitly enabled SACL tier.</summary>
    internal sealed class SecurityAuditAttributionService : BackgroundService
    {
        private readonly SaclAttributionOptions options;
        private readonly IAuditPolicyConformance policy;
        private readonly IHealthReporter health;
        private readonly ILogger<SecurityAuditAttributionService> logger;
        private EventLogWatcher? watcher;

        public SecurityAuditAttributionService(IOptions<SaclAttributionOptions> options,
            IAuditPolicyConformance policy, IHealthReporter health,
            ILogger<SecurityAuditAttributionService> logger)
        {
            this.options = options.Value;
            this.policy = policy;
            this.health = health;
            this.logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            options.Validate();
            if (!options.Enabled) return base.StartAsync(cancellationToken);

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
            return base.StartAsync(cancellationToken);
        }

        private void RequirePolicy(Guid subcategory, string name)
        {
            if (policy.IsEnabled(subcategory, out var reason)) return;
            health.CoverageGap("SACLAttribution", name, $"AuditPolicyMissing:{reason}", 0);
            throw new InvalidOperationException($"SACL attribution requires the '{name}' audit subcategory: {reason}");
        }

        private void OnRecord(object? sender, EventRecordWrittenEventArgs args)
        {
            if (args.EventException != null)
            {
                health.CoverageGap("SACLAttribution", "Security", $"ReadFailure:{args.EventException.GetType().Name}");
                return;
            }
            using var record = args.EventRecord;
            // Preserve native XML: it contains SubjectUserSid/Name and, for 4657, old/new values.
            var xml = record?.ToXml();
            if (xml == null || !IsDeclaredScope(xml)) return;
            logger.LogInformation("SACL ATTRIBUTION EventId={EventId} NativeEvidence={NativeEvidence}",
                record?.Id, xml);
        }

        private bool IsDeclaredScope(string xml)
        {
            var document = XDocument.Parse(xml);
            var eventData = document.Descendants().Where(element => element.Name.LocalName == "Data");
            var objectName = eventData.FirstOrDefault(element =>
                (string?)element.Attribute("Name") is "ObjectName" or "KeyName")?.Value;
            if (string.IsNullOrWhiteSpace(objectName)) return false;
            return options.FileScopes.Concat(options.RegistryScopes).Any(scope =>
                objectName.StartsWith(scope, StringComparison.OrdinalIgnoreCase));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);

        public override void Dispose()
        {
            if (watcher != null) watcher.EventRecordWritten -= OnRecord;
            watcher?.Dispose();
            base.Dispose();
        }
    }
}
