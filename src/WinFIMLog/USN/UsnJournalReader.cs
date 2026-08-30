using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinFIMLog.Utils;

namespace WinFIMLog.USN
{
    /// <summary>Outcome of one journal read.</summary>
    internal enum UsnReadStatus
    {
        Succeeded,

        /// <summary>No active change journal on this volume, or it is not NTFS.</summary>
        JournalUnavailable,

        /// <summary>The volume handle is gone; the volume was removed or locked.</summary>
        VolumeUnavailable,

        /// <summary>The read failed for a reason that does not invalidate the cursor.</summary>
        ReadFailed
    }

    internal sealed class UsnReadResult
    {
        public UsnReadStatus Status { get; init; }
        public IReadOnlyList<ParsedUsnRecord> Records { get; init; } = [];

        /// <summary>Position the next read starts from. Meaningful only when the read succeeded.</summary>
        public long NextUsn { get; init; }
    }

    /// <summary>Reads USN_RECORD_V2 entries from one NTFS volume's change journal.</summary>
    /// <remarks>
    /// One instance owns one volume handle and one <see cref="DirectoryPathCache"/>. It is not
    /// thread-safe; a replay drives one volume from one loop.
    /// </remarks>
    internal sealed class UsnJournalReader : IDisposable
    {
        private const int ReadBufferBytes = 64 * 1024;
        private const uint AllReasons = 0xFFFFFFFF;
        private const int NextUsnHeaderBytes = sizeof(long);

        private readonly char driveLetter;
        private readonly ILogger logger;
        private readonly byte[] buffer = new byte[ReadBufferBytes];
        private IntPtr volumeHandle = IntPtr.Zero;
        private DirectoryPathCache? pathCache;
        private bool disposed;

        public UsnJournalReader(char driveLetter, ILogger logger)
        {
            this.driveLetter = char.ToUpperInvariant(driveLetter);
            this.logger = logger;
        }

        public char DriveLetter => driveLetter;

        /// <summary>Volume serial, so a cursor survives a drive-letter reassignment.</summary>
        public string VolumeSerialNumber { get; private set; } = string.Empty;

        public ulong JournalId { get; private set; }

        public DirectoryPathCache? PathCache => pathCache;

        public bool TryOpen()
        {
            if (volumeHandle != IntPtr.Zero)
            {
                return true;
            }

            VolumeSerialNumber = ReadVolumeSerial(driveLetter);

            // The device path form carries no trailing separator; CreateFileW rejects "\\.\C:\".
            var handle = NativeMethods.CreateFileW($@"\\.\{driveLetter}:", NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero,
                NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                logger.LogDebug("Could not open volume {Drive}: for journal reading, error {Error}",
                    driveLetter, Marshal.GetLastWin32Error());
                return false;
            }

            volumeHandle = handle;
            pathCache = new DirectoryPathCache(driveLetter, volumeHandle, logger);
            return true;
        }

        /// <summary>Reads the journal's identity and retained range.</summary>
        public bool TryQueryJournal(out NativeMethods.UsnJournalData journal)
        {
            journal = default;
            if (volumeHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!NativeMethods.DeviceIoControl(volumeHandle, NativeMethods.FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero, 0, out journal,
                    (uint)Marshal.SizeOf<NativeMethods.UsnJournalData>(), out _, IntPtr.Zero))
            {
                logger.LogDebug("Journal query failed on {Drive}: with error {Error}",
                    driveLetter, Marshal.GetLastWin32Error());
                return false;
            }

            JournalId = journal.UsnJournalID;
            return true;
        }

        /// <summary>Reads one buffer of records starting at <paramref name="startUsn"/>.</summary>
        public UsnReadResult Read(long startUsn)
        {
            if (volumeHandle == IntPtr.Zero)
            {
                return new UsnReadResult { Status = UsnReadStatus.VolumeUnavailable };
            }

            var input = new NativeMethods.ReadUsnJournalData
            {
                StartUsn = startUsn,
                ReasonMask = AllReasons,

                // Reporting a change when it happens matters more than waiting for the writing
                // handle to close; a transient file may never be closed cleanly at all.
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = JournalId,
                MinMajorVersion = 2,
                MaxMajorVersion = 2
            };

            if (!NativeMethods.DeviceIoControl(volumeHandle, NativeMethods.FSCTL_READ_USN_JOURNAL,
                    ref input, (uint)Marshal.SizeOf<NativeMethods.ReadUsnJournalData>(),
                    buffer, ReadBufferBytes, out var bytesReturned, IntPtr.Zero))
            {
                return new UsnReadResult { Status = ClassifyReadFailure(Marshal.GetLastWin32Error()) };
            }

            // The first eight bytes are the position to resume from; records follow.
            if (bytesReturned < NextUsnHeaderBytes)
            {
                return new UsnReadResult { Status = UsnReadStatus.ReadFailed };
            }

            var nextUsn = BitConverter.ToInt64(buffer, 0);
            var payloadBytes = (int)bytesReturned - NextUsnHeaderBytes;
            if (payloadBytes <= 0)
            {
                return new UsnReadResult { Status = UsnReadStatus.Succeeded, NextUsn = nextUsn };
            }

            var payload = new byte[payloadBytes];
            Buffer.BlockCopy(buffer, NextUsnHeaderBytes, payload, 0, payloadBytes);

            var parser = new UsnRecordParser(payload);
            var records = new List<ParsedUsnRecord>();
            while (parser.TryReadNext(out var record, out _))
            {
                records.Add(record);
            }

            return new UsnReadResult
            {
                Status = UsnReadStatus.Succeeded,
                Records = records,
                NextUsn = nextUsn
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pathCache?.Dispose();
            pathCache = null;

            if (volumeHandle != IntPtr.Zero && volumeHandle != new IntPtr(-1))
            {
                NativeMethods.CloseHandle(volumeHandle);
            }

            volumeHandle = IntPtr.Zero;
        }

        internal static UsnReadStatus ClassifyReadFailure(int win32Error) => win32Error switch
        {
            NativeMethods.ERROR_JOURNAL_NOT_ACTIVE or
            NativeMethods.ERROR_JOURNAL_DELETE_IN_PROGRESS or
            NativeMethods.ERROR_INVALID_FUNCTION => UsnReadStatus.JournalUnavailable,
            _ => UsnReadStatus.ReadFailed
        };

        private static string ReadVolumeSerial(char driveLetter)
        {
            try
            {
                return NativeMethods.GetVolumeInformationW($@"{driveLetter}:\", null, 0,
                    out var serial, out _, out _, null, 0)
                    ? serial.ToString("X8")
                    : string.Empty;
            }
            catch (Exception)
            {
                // Identity is a convenience for cursor keying; its absence must not stop monitoring.
                return string.Empty;
            }
        }
    }
}
