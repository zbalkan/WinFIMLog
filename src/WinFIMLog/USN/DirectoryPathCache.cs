using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WinFIMLog.Utils;

namespace WinFIMLog.USN
{
    /// <summary>Resolves parent directory paths from NTFS file reference numbers.</summary>
    /// <remarks>
    /// A journal record names its parent directory by file id, not by path, so every record needs an
    /// <c>OpenFileById</c> plus <c>GetFinalPathNameByHandle</c> pair to become a path. Caching by
    /// parent reference collapses that to one resolution per directory rather than one per file,
    /// which is the difference that makes a replay of a busy volume affordable.
    ///
    /// Resolution fails when the parent directory has itself been deleted. That is not an error
    /// case to suppress: it is the transient activity Tier 0.5 exists to catch, so the caller gets a
    /// marked placeholder rather than nothing.
    /// </remarks>
    public sealed class DirectoryPathCache : IDisposable
    {
        private const int MaxCacheSize = 20_000;
        private const int MaxPathChars = 260;

        private readonly Dictionary<ulong, string> pathCache = new();
        private readonly char driveLetter;
        private readonly IntPtr volumeHandle;
        private readonly ILogger? logger;

        public DirectoryPathCache(char driveLetter, IntPtr volumeHandle, ILogger? logger = null)
        {
            this.driveLetter = char.ToUpperInvariant(driveLetter);
            this.volumeHandle = volumeHandle;
            this.logger = logger;
        }

        /// <summary>Path of a parent directory, or a placeholder when it can no longer be reached.</summary>
        public string GetPath(ulong parentFileRef)
        {
            if (parentFileRef == 0)
            {
                return $@"{driveLetter}:\";
            }

            lock (pathCache)
            {
                if (pathCache.TryGetValue(parentFileRef, out var cached))
                {
                    return cached;
                }
            }

            var resolved = Resolve(parentFileRef);
            if (resolved is null)
            {
                return $@"{driveLetter}:\?";
            }

            lock (pathCache)
            {
                // Clearing on overflow costs re-resolution of a working set that is about to turn
                // over anyway. Keeping an eviction order to avoid that is not worth its own bugs.
                if (pathCache.Count >= MaxCacheSize)
                {
                    pathCache.Clear();
                }

                pathCache[parentFileRef] = resolved;
            }

            return resolved;
        }

        private string? Resolve(ulong fileRef)
        {
            var handle = IntPtr.Zero;
            try
            {
                // Type = FileId selects the 64-bit LARGE_INTEGER union member; Size must be the
                // full FILE_ID_DESCRIPTOR size, not just the bytes this call happens to populate,
                // because that is what a real caller passes and what Size documents (ADR-0021).
                var fileId = new NativeMethods.FileIdDescriptor
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.FileIdDescriptor>(),
                    Type = NativeMethods.FileId,
                    FileIdValue = unchecked((long)fileRef)
                };

                handle = NativeMethods.OpenFileById(volumeHandle, ref fileId, NativeMethods.GENERIC_READ,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero,
                    NativeMethods.FILE_FLAG_BACKUP_SEMANTICS);

                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    return null;
                }

                var buffer = new char[MaxPathChars];
                var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length,
                    NativeMethods.VOLUME_NAME_DOS);

                if (length == 0)
                {
                    return null;
                }

                var path = new string(buffer, 0, (int)Math.Min(length, buffer.Length - 1));

                // GetFinalPathNameByHandle returns the extended-length form; monitored scopes are
                // configured in the ordinary form, so the prefix has to come off before matching.
                return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
            }
            catch (Exception exception)
            {
                logger?.LogDebug(exception, "Could not resolve file reference {FileRef:X16} on {Drive}:",
                    fileRef, driveLetter);
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    NativeMethods.CloseHandle(handle);
                }
            }
        }

        public void Dispose()
        {
            lock (pathCache)
            {
                pathCache.Clear();
            }
        }
    }
}
