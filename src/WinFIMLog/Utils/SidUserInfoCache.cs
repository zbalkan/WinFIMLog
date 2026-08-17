using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WinFIMLog.Utils
{
    /// <summary>
    /// Caches user information by security identifier. A SID can be shared by many short-lived
    /// processes, so keying the cache by process would duplicate the same principal information.
    /// </summary>
    internal static class SidUserInfoCache
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> Entries =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        internal static UserInformation Get(Process process)
        {
            ArgumentNullException.ThrowIfNull(process);

            using var owner = process.Owner();
            var sid = owner.User?.Value;
            var now = DateTime.UtcNow;
            if (sid != null && Entries.TryGetValue(sid, out var cached) && cached.ExpiresAt > now)
            {
                return cached.User;
            }

            var user = new UserInformation(
                sid,
                owner.Name,
                owner.AuthenticationType,
                owner.IsAuthenticated,
                owner.IsSystem);

            if (sid != null)
            {
                Entries[sid] = new CacheEntry(user, now.Add(Lifetime));
                RemoveExpiredEntries(now);
            }

            return user;
        }

        private static void RemoveExpiredEntries(DateTime now)
        {
            if (Entries.Count < 1024)
            {
                return;
            }

            foreach (var entry in Entries)
            {
                if (entry.Value.ExpiresAt <= now)
                {
                    Entries.TryRemove(entry.Key, out _);
                }
            }
        }

        private sealed record CacheEntry(UserInformation User, DateTime ExpiresAt);
    }

    internal sealed record UserInformation(
        string? SID,
        string Username,
        string? AuthenticationType,
        bool IsAuthenticated,
        bool IsSystem);
}
