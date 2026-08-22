using System;
using System.IO;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Data;
using WinFIMLog.Events;
using WinFIMLog.Health;
using WinFIMLog.IO;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class TpmFallbackHealthTests
{
    [TestMethod]
    public void Registry_fallback_event_reports_registry_v2()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"winfimlog-tpm-health-{Guid.NewGuid():N}.db");
        using var context = new LiteDbContext(Options.Create(new LiteDbOptions { DatabasePath = databasePath }));
        try
        {
            var sink = new RecordingSink();
            var settings = new Settings(SettingsAtomicPublicationTests.GenerationForTest("scope", Path.GetTempPath()));
            var reporter = new HealthReporter(NullLogger<HealthReporter>.Instance, settings, sink,
                new SnapshotHealthState(), new EventOutboxRepository(context));

            reporter.TpmIntegrityUnavailable("scope", "PlatformCryptoProviderUnavailable", BaselineAlgorithm.RegistryV2);

            Assert.IsNotNull(sink.Record);
            Assert.AreEqual("TpmIntegrityUnavailable", sink.Record.RecordType);
            Assert.AreEqual((int)BaselineAlgorithm.RegistryV2, sink.Record.Fields["fallbackAlgorithm"]);
            Assert.IsTrue(sink.Error);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            if (File.Exists(databasePath + "-log"))
            {
                File.Delete(databasePath + "-log");
            }
        }
    }

    private sealed class RecordingSink : ILocalEventSink
    {
        public bool Error { get; private set; }
        public EventContract? Record { get; private set; }

        public void Write(EventContract record, bool error = false)
        {
            Record = record;
            Error = error;
        }
    }
}
