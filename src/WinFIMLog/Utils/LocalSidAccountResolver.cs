using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace WinFIMLog.Utils
{
    /// <summary>
    /// Resolves SIDs from logon-session data already held by the local LSA. Unlike account
    /// translation, this lookup does not need a domain controller.
    /// </summary>
    internal static partial class LocalSidAccountResolver
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> Entries =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
        private static readonly Lock RefreshLock = new();
        private static DateTime _nextRefreshUtc;

        internal static string? Resolve(string sid)
        {
            ArgumentException.ThrowIfNullOrEmpty(sid);

            var now = DateTime.UtcNow;
            if (Entries.TryGetValue(sid, out var cached) && cached.ExpiresAt > now)
            {
                return cached.AccountName;
            }

            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            lock (RefreshLock)
            {
                if (now >= _nextRefreshUtc)
                {
                    Refresh(now);
                    _nextRefreshUtc = now.Add(Lifetime);
                }
            }

            return Entries.TryGetValue(sid, out cached) && cached.ExpiresAt > now
                ? cached.AccountName
                : null;
        }

        [LibraryImport("secur32.dll")]
        private static partial uint LsaEnumerateLogonSessions(out ulong logonSessionCount, out IntPtr logonSessionList);

        [LibraryImport("secur32.dll")]
        private static partial uint LsaFreeReturnBuffer(IntPtr buffer);

        [LibraryImport("secur32.dll")]
        private static partial uint LsaGetLogonSessionData(ref Luid logonId, out IntPtr ppLogonSessionData);

        private static void Refresh(DateTime now)
        {
            if (LsaEnumerateLogonSessions(out var count, out var sessions) != 0)
            {
                return;
            }

            try
            {
                var luidSize = Marshal.SizeOf<Luid>();
                for (ulong index = 0; index < count; index++)
                {
                    var luidAddress = IntPtr.Add(sessions, checked((int)(index * (ulong)luidSize)));
                    var luid = Marshal.PtrToStructure<Luid>(luidAddress);
                    if (LsaGetLogonSessionData(ref luid, out var dataAddress) != 0 || dataAddress == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        var data = Marshal.PtrToStructure<SecurityLogonSessionData>(dataAddress);
                        if (data.Sid == IntPtr.Zero)
                        {
                            continue;
                        }

                        var sid = new SecurityIdentifier(data.Sid).Value;
                        var user = data.UserName.ToString();
                        var domain = data.LogonDomain.ToString();
                        var upn = data.Upn.ToString();
                        var accountName = !string.IsNullOrWhiteSpace(user)
                            ? string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}"
                            : upn;

                        if (!string.IsNullOrWhiteSpace(accountName))
                        {
                            Entries[sid] = new CacheEntry(accountName, now.Add(Lifetime));
                        }
                    }
                    catch (ArgumentException)
                    {
                        // A session may disappear, leaving data that cannot be converted to a SID.
                    }
                    finally
                    {
                        _ = LsaFreeReturnBuffer(dataAddress);
                    }
                }
            }
            finally
            {
                _ = LsaFreeReturnBuffer(sessions);
            }
        }

        private sealed record CacheEntry(string AccountName, DateTime ExpiresAt);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct LsaUnicodeString
        {
            private readonly ushort Length;
            private readonly ushort MaximumLength;
            private readonly IntPtr Buffer;

            public override string ToString() =>
                Buffer == IntPtr.Zero || Length == 0
                    ? string.Empty
                    : Marshal.PtrToStringUni(Buffer, Length / sizeof(char)) ?? string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            internal uint LowPart;
            internal int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityLogonSessionData
        {
            internal uint Size;
            internal Luid LogonId;
            internal LsaUnicodeString UserName;
            internal LsaUnicodeString LogonDomain;
            internal LsaUnicodeString AuthenticationPackage;
            internal uint LogonType;
            internal uint Session;
            internal IntPtr Sid;
            internal long LogonTime;
            internal LsaUnicodeString LogonServer;
            internal LsaUnicodeString DnsDomainName;
            internal LsaUnicodeString Upn;
        }
    }
}
