using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinFIMLog.Utils;

namespace WinFIMLog.USN
{
    /// <summary>Outcome of a single journal read attempt for one volume.</summary>
    internal enum UsnReadStatus
    {
        Succeeded,

        /// <summary>The volume has no active change journal, or is not NTFS.</summary>
        JournalUnavailable,

        /// <summary>The volume handle could not be opened, usually a removed or locked volume.</summary>
        VolumeUnavailable,

        /// <summary>The read itself failed for a reason that does not invalidate the cursor.</summary>
        ReadFailed
    }

    /// <summary>Records read from one poll, plus where the cursor should now sit.</summary>
    internal sealed class UsnReadResult
    {
        public UsnReadStatus Status { get; init; }
        public IReadOnlyList<ParsedUsnRecord> Records { get; init; } = [];

        /// <summary>USN the next read should start from. Only meaningful when <see cref="Status"/> succeeded.</summary>
        public long NextUsn { get; init; }

        /// <summary>Records held back because they are newer than the settle threshold.</summary>
        public int DeferredCount { get; init; }
    }

    /// <summary>
    /// Reads USN_RECORD_V2 entries from one NTFS volume's change journal.
    /// </summary>
    /// <remarks>
    /// One instance owns one volume handle and one <see cref="DirectoryPathCache"/> for the lifetime
    /// of that volume's monitoring. The reader is not thread-safe; the owning job polls one volume
    /// from one loop.
    ///
    /// Reads are deliberately held back from the head of the journal by a settle threshold. A record
    /// is only processed once it is older than that threshold, which gives the FileSystemWatcher path
    /// time to admit and enrich the same operation first. Without it, a poll that lands microseconds
    /// after a write would publish a USN record for an operation Tier 1 is still enriching, producing
    /// a duplicate that carries no attribution. Deferred records are not skipped: the cursor stops
    /// short of them and they are re-read on the following poll.
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

        /// <summary>Volume serial, used to key the cursor so a drive-letter change does not orphan it.</summary>
        public string VolumeSerialNumber { get; private set; } = string.Empty;

        public ulong JournalId { get; private set; }

        /// <summary>Path resolution cache for this volume; null until <see cref="TryOpen"/> succeeds.</summary>
        public DirectoryPathCache? PathCache => pathCache;

        /// <summary>Opens the volume handle and reads its identity.</summary>
        public bool TryOpen()
        {
            if (volumeHandle != IntPtr.Zero)
            {
                return true;
            }

            VolumeSerialNumber = ReadVolumeSerial(driveLetter);

            // The device path form has no trailing separator; CreateFileW rejects "\\.\C:\".
            var devicePath = $@"\\.\{driveLetter}:";
            var handle = NativeMethods.CreateFileW(
                devicePath,
                NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                logger.LogDebug("Could not open volume {Drive}: for journal reading, error {Error}",
                    driveLetter, Marshal.GetLastWin32Error());
                return false;
            }

            volumeHandle = handle;
            pathCache = new DirectoryPathCache(driveLetter, volumeHandle);
            return true;
        }

        /// <summary>Queries the journal's identity and retained range.</summary>
        public bool TryQueryJournal(out NativeMethods.UsnJournalData journal)
        {
            journal = default;
            if (volumeHandle == IntPtr.Zero)
            {
                return false;
            }

            var ok = NativeMethods.DeviceIoControl(
                volumeHandle,
                NativeMethods.FSCTL_QUERY_USN_JOURNAL,
                IntPtr.Zero,
                0,
                out journal,
                (uint)Marshal.SizeOf<NativeMethods.UsnJournalData>(),
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                logger.LogDebug("Journal query failed on {Drive}: with error {Error}", driveLetter, error);
                return false;
            }

            JournalId = journal.UsnJournalID;
            return true;
        }

        /// <summary>Reads one buffer of records starting at <paramref name="startUsn"/>.</summary>
        /// <param name="startUsn">First USN to read; records at or after this position are returned.</param>
        /// <param name="settleThreshold">
        /// Records with a timestamp at or after this instant are deferred to a later poll so Tier 1
        /// can claim them first.
        /// </param>
        public UsnReadResult Read(long startUsn, DateTime settleThreshold)
        {
            if (volumeHandle == IntPtr.Zero)
            {
                return new UsnReadResult { Status = UsnReadStatus.VolumeUnavailable };
            }

            var input = new NativeMethods.ReadUsnJournalData
            {
                StartUsn = startUsn,
                ReasonMask = AllReasons,

                // Reporting the change when it happens matters more than waiting for the writing
                // handle to close; a transient file may never be closed cleanly at all.
                ReturnOnlyOnClose = 0,
                Timeout = 0,

                // Return whatever is available instead of blocking until a byte threshold is met.
                BytesToWaitFor = 0,
                UsnJournalID = JournalId,
                MinMajorVersion = 2,
                MaxMajorVersion = 2
            };

            var ok = NativeMethods.DeviceIoControl(
                volumeHandle,
                NativeMethods.FSCTL_READ_USN_JOURNAL,
                ref input,
                (uint)Marshal.SizeOf<NativeMethods.ReadUsnJournalData>(),
                buffer,
                ReadBufferBytes,
                out var bytesReturned,
                IntPtr.Zero);

            if (!ok)
            {
                return new UsnReadResult { Status = ClassifyReadFailure(Marshal.GetLastWin32Error()) };
            }

            // The first eight bytes are the USN to resume from; records follow.
            if (bytesReturned < NextUsnHeaderBytes)
            {
                return new UsnReadResult { Status = UsnReadStatus.ReadFailed };
            }

            var journalNextUsn = BitConverter.ToInt64(buffer, 0);
            var payloadBytes = (int)bytesReturned - NextUsnHeaderBytes;
            if (payloadBytes <= 0)
            {
                return new UsnReadResult
                {
                    Status = UsnReadStatus.Succeeded,
                    NextUsn = journalNextUsn
                };
            }

            var payload = new byte[payloadBytes];
            Buffer.BlockCopy(buffer, NextUsnHeaderBytes, payload, 0, payloadBytes);

            var parser = new UsnRecordParser(payload);
            var records = new List<ParsedUsnRecord>();
            var deferred = 0;
            var resumeUsn = journalNextUsn;

            while (parser.TryReadNext(out var record, out _))
            {
                if (record.GetDateTimeUtc() >= settleThreshold)
                {
                    // Stop at the first unsettled record and leave the cursor on it, so this record
                    // and everything after it is re-read once it has had time to settle.
                    resumeUsn = record.Usn;
                    deferred = 1;
                    break;
                }

                records.Add(record);
            }

            return new UsnReadResult
            {
                Status = UsnReadStatus.Succeeded,
                Records = records,
                NextUsn = resumeUsn,
                DeferredCount = deferred
            };
        }

        /// <summary>Drops the path cache, used when a volume disappears without the job stopping.</summary>
        public void ResetCache() => pathCache?.Clear();

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

            // The requested start position has been trimmed away. The cursor policy re-floors it.
            NativeMethods.ERROR_JOURNAL_ENTRY_DELETED => UsnReadStatus.ReadFailed,

            _ => UsnReadStatus.ReadFailed
        };

        private static string ReadVolumeSerial(char driveLetter)
        {
            try
            {
                var ok = NativeMethods.GetVolumeInformationW($"{driveLetter}:\\", null, 0,
                    out var serial, out _, out _, null, 0);
                return ok ? serial.ToString("X8") : string.Empty;
            }
            catch (Exception)
            {
                // Identity is a convenience for cursor keying; its absence must not stop monitoring.
                return string.Empty;
            }
        }
    }
}
