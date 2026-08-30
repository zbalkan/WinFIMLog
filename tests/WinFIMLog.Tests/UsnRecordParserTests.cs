using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinFIMLog.USN;
using WinFIMLog.Utils;

namespace WinFIMLog.Tests;

[TestClass]
public sealed class UsnRecordParserTests
{
    /// <summary>
    /// The Win32 USN_RECORD_V2 header is 60 bytes. Declaring FileNameLength or FileNameOffset as
    /// DWORD instead of WORD moves every subsequent offset and silently misparses every record, so
    /// the offsets are asserted directly rather than inferred from the managed struct.
    /// </summary>
    [TestMethod]
    public void Record_header_field_offsets_match_the_win32_layout()
    {
        Assert.AreEqual(0, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.RecordLength)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.FileReferenceNumber)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.ParentDirectoryReferenceNumber)));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.Usn)));
        Assert.AreEqual(40, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.Reason)));
        Assert.AreEqual(56, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.FileNameLength)));
        Assert.AreEqual(58, (int)Marshal.OffsetOf<NativeMethods.UsnRecord>(nameof(NativeMethods.UsnRecord.FileNameOffset)));
    }

    [TestMethod]
    public void Single_record_round_trips_with_its_filename()
    {
        var timestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var buffer = BuildRecord(usn: 4096, parentRef: 0x1234, filename: "payload.tmp",
            reason: (uint)NativeMethods.UsnReasonFlags.FileCreate, timestamp: timestamp);

        var parser = new UsnRecordParser(buffer);

        Assert.IsTrue(parser.TryReadNext(out var record, out var filename));
        Assert.AreEqual("payload.tmp", filename);
        Assert.AreEqual("payload.tmp", record.Filename);
        Assert.AreEqual(4096, record.Usn);
        Assert.AreEqual(0x1234UL, record.ParentDirectoryReferenceNumber);
        Assert.AreEqual((uint)NativeMethods.UsnReasonFlags.FileCreate, record.Reason);
        Assert.AreEqual(timestamp, record.GetDateTimeUtc());
        Assert.IsFalse(parser.TryReadNext(out _, out _));
    }

    [TestMethod]
    public void Consecutive_records_are_parsed_in_order()
    {
        var first = BuildRecord(10, 1, "first.txt", (uint)NativeMethods.UsnReasonFlags.FileCreate);
        var second = BuildRecord(20, 1, "second.txt", (uint)NativeMethods.UsnReasonFlags.FileDelete);
        var third = BuildRecord(30, 2, "third.txt", (uint)NativeMethods.UsnReasonFlags.DataOverwrite);

        var buffer = Concat(first, second, third);
        var records = ParseAll(buffer);

        Assert.AreEqual(3, records.Count);
        CollectionAssert.AreEqual(new long[] { 10, 20, 30 },
            records.ConvertAll(record => record.Usn));
        CollectionAssert.AreEqual(new[] { "first.txt", "second.txt", "third.txt" },
            records.ConvertAll(record => record.Filename));
    }

    [TestMethod]
    public void Truncated_trailing_record_is_refused_rather_than_misread()
    {
        var complete = BuildRecord(10, 1, "complete.txt", (uint)NativeMethods.UsnReasonFlags.FileCreate);
        var partial = BuildRecord(20, 1, "partial.txt", (uint)NativeMethods.UsnReasonFlags.FileCreate);

        // Cut the second record short, as a buffer boundary would.
        var truncated = new byte[complete.Length + 24];
        Buffer.BlockCopy(complete, 0, truncated, 0, complete.Length);
        Buffer.BlockCopy(partial, 0, truncated, complete.Length, 24);

        var records = ParseAll(truncated);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("complete.txt", records[0].Filename);
    }

    [TestMethod]
    public void Record_claiming_a_length_beyond_the_buffer_is_refused()
    {
        var buffer = BuildRecord(10, 1, "x.txt", (uint)NativeMethods.UsnReasonFlags.FileCreate);
        BitConverter.GetBytes((uint)(buffer.Length + 512)).CopyTo(buffer, 0);

        Assert.IsFalse(new UsnRecordParser(buffer).TryReadNext(out _, out _));
    }

    [TestMethod]
    public void Filename_extending_past_the_record_is_refused()
    {
        var buffer = BuildRecord(10, 1, "x.txt", (uint)NativeMethods.UsnReasonFlags.FileCreate);
        BitConverter.GetBytes((ushort)512).CopyTo(buffer, 56);

        Assert.IsFalse(new UsnRecordParser(buffer).TryReadNext(out _, out _));
    }

    private static List<ParsedUsnRecord> ParseAll(byte[] buffer)
    {
        var parser = new UsnRecordParser(buffer);
        var records = new List<ParsedUsnRecord>();
        while (parser.TryReadNext(out var record, out _))
        {
            records.Add(record);
        }

        return records;
    }

    internal static byte[] BuildRecord(long usn, ulong parentRef, string filename, uint reason,
        DateTime? timestamp = null, uint fileAttributes = 0)
    {
        var nameBytes = Encoding.Unicode.GetBytes(filename);
        var length = NativeMethods.UsnRecordV2HeaderBytes + nameBytes.Length;
        var buffer = new byte[length];

        BitConverter.GetBytes((uint)length).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
        BitConverter.GetBytes(0xAAAAUL).CopyTo(buffer, 8);
        BitConverter.GetBytes(parentRef).CopyTo(buffer, 16);
        BitConverter.GetBytes(usn).CopyTo(buffer, 24);
        BitConverter.GetBytes((timestamp ?? DateTime.UtcNow).ToFileTimeUtc()).CopyTo(buffer, 32);
        BitConverter.GetBytes(reason).CopyTo(buffer, 40);
        BitConverter.GetBytes(0u).CopyTo(buffer, 44);
        BitConverter.GetBytes(0u).CopyTo(buffer, 48);
        BitConverter.GetBytes(fileAttributes).CopyTo(buffer, 52);
        BitConverter.GetBytes((ushort)nameBytes.Length).CopyTo(buffer, 56);
        BitConverter.GetBytes((ushort)NativeMethods.UsnRecordV2HeaderBytes).CopyTo(buffer, 58);
        nameBytes.CopyTo(buffer, NativeMethods.UsnRecordV2HeaderBytes);

        return buffer;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var buffer = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
            offset += part.Length;
        }

        return buffer;
    }
}
