using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.Events;
using WinFIMLog.IO;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class BurstAggregatingEventSinkTests
{
    [TestMethod]
    public void Excess_findings_are_replaced_by_a_complete_aggregation_record()
    {
        var writer = new RecordingWriter();
        var sink = new BurstAggregatingEventSink(writer, Options.Create(new BurstAggregationOptions
        { Threshold = 2, WindowSeconds = 1 }));
        var started = DateTimeOffset.UtcNow;

        for (var index = 0; index < 5; index++)
            sink.Write(EventContract.Create(7777, "FileSystemFinding", $"record-{index}", "scope", new Dictionary<string, object?>()));
        sink.Flush(started.AddSeconds(2));

        Assert.AreEqual(3, writer.Records.Count);
        Assert.AreEqual("Aggregation", writer.Records[2].RecordType);
        Assert.AreEqual(3L, writer.Records[2].Fields["count"]);
        Assert.AreEqual("record-4", writer.Records[2].Fields["sampleRecordId"]);
    }

    private sealed class RecordingWriter : IEventRecordWriter
    {
        internal List<EventContract> Records { get; } = [];
        public void Write(EventContract record, bool error = false) => Records.Add(record);
    }
}
