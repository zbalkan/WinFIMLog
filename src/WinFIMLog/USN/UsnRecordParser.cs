using System;
using System.Runtime.InteropServices;
using System.Text;
using WinFIMLog.Utils;

namespace WinFIMLog.USN
{
    /// <summary>Parses USN_RECORD_V2 entries out of a journal read buffer.</summary>
    /// <remarks>
    /// Each record is a 60-byte header followed by a variable-length UTF-16LE filename, and carries
    /// its own length, so records are walked sequentially from the start of the buffer.
    /// </remarks>
    public sealed class UsnRecordParser
    {
        private readonly byte[] buffer;
        private int position;

        public UsnRecordParser(byte[] buffer)
        {
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.position = 0;
        }

        /// <summary>Attempts to parse the next USN record from the buffer.</summary>
        /// <param name="record">Output: the parsed USN record</param>
        /// <param name="filename">Output: the UTF-16LE decoded filename</param>
        /// <returns>True if a record was successfully parsed; false if buffer exhausted or truncated</returns>
        public bool TryReadNext(out ParsedUsnRecord record, out string filename)
        {
            record = new ParsedUsnRecord();
            filename = string.Empty;

            // The declared header is 60 bytes. Marshal.SizeOf would report the CLR's padded size,
            // which is larger and would reject a final record that is entirely valid.
            const int headerSize = NativeMethods.UsnRecordV2HeaderBytes;
            if (position + headerSize > buffer.Length)
                return false;

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var headerPtr = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, position);
                var nativeRecord = Marshal.PtrToStructure<NativeMethods.UsnRecord>(headerPtr);

                // Validate record length
                if (nativeRecord.RecordLength < headerSize || nativeRecord.RecordLength > buffer.Length - position)
                    return false;

                // Extract filename
                var filenameOffset = (int)nativeRecord.FileNameOffset;
                var filenameLength = (int)nativeRecord.FileNameLength;

                if (filenameOffset + filenameLength > nativeRecord.RecordLength)
                    return false;

                if (filenameLength > 0)
                {
                    try
                    {
                        var filenameBytes = new byte[filenameLength];
                        Buffer.BlockCopy(buffer, position + filenameOffset, filenameBytes, 0, filenameLength);
                        filename = Encoding.Unicode.GetString(filenameBytes);
                    }
                    catch
                    {
                        filename = "(decode error)";
                    }
                }

                // Populate output record
                record = new ParsedUsnRecord
                {
                    ParentDirectoryReferenceNumber = nativeRecord.ParentDirectoryReferenceNumber,
                    Usn = nativeRecord.Usn,
                    TimeStamp = nativeRecord.TimeStamp,
                    Reason = nativeRecord.Reason,
                    Filename = filename
                };

                // Advance position to next record
                position += (int)nativeRecord.RecordLength;
                return true;
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

    }

    /// <summary>Represents a successfully parsed USN_RECORD_V2 entry.</summary>
    public sealed class ParsedUsnRecord
    {
        public ulong ParentDirectoryReferenceNumber { get; set; }
        public long Usn { get; set; }
        public long TimeStamp { get; set; }  // 100-nanosecond intervals since 1/1/1601
        public uint Reason { get; set; }
        public string Filename { get; set; } = string.Empty;

        /// <summary>Converts TimeStamp (FILETIME) to DateTime in UTC.</summary>
        public DateTime GetDateTimeUtc()
        {
            try
            {
                return DateTime.FromFileTimeUtc(TimeStamp);
            }
            catch
            {
                return DateTime.UnixEpoch;
            }
        }
    }
}
