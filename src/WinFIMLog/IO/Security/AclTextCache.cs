using System;
using System.Collections.Generic;
using System.Threading;
using System.Security.Principal;

namespace WinFIMLog.IO.Security
{
    /// <summary>
    /// Bounded cache for already-rendered ACL text. Cache keys are typed ACL snapshots, so a hit
    /// is confirmed structurally before formatting any candidate string.
    /// </summary>
    internal sealed class AclTextCache
    {
        private const int Capacity = 4_096;
        private const int MaximumCachedAceCount = 65_536;
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        private readonly Dictionary<AclFingerprint, List<Entry>> buckets = [];
        private readonly Lock cacheLock = new();
        private int count;
        private int cachedAceCount;

        public bool TryGet(AccessControlList accessControlList, out string text)
        {
            ArgumentNullException.ThrowIfNull(accessControlList);
            var fingerprint = AclFingerprint.Create(accessControlList);
            var now = DateTime.UtcNow;

            lock (cacheLock)
            {
                if (!buckets.TryGetValue(fingerprint, out var bucket))
                {
                    text = string.Empty;
                    return false;
                }

                for (var index = bucket.Count - 1; index >= 0; index--)
                {
                    var entry = bucket[index];
                    if (entry.ExpiresAt <= now)
                    {
                        bucket.RemoveAt(index);
                        count--;
                        cachedAceCount -= entry.AceCount;
                        continue;
                    }

                    if (entry.Matches(accessControlList))
                    {
                        text = entry.Text;
                        return true;
                    }
                }

                if (bucket.Count is 0)
                {
                    buckets.Remove(fingerprint);
                }
            }

            text = string.Empty;
            return false;
        }

        public string Add(AccessControlList accessControlList, string text)
        {
            ArgumentNullException.ThrowIfNull(accessControlList);
            ArgumentNullException.ThrowIfNull(text);

            var fingerprint = AclFingerprint.Create(accessControlList);
            var now = DateTime.UtcNow;
            lock (cacheLock)
            {
                if (buckets.TryGetValue(fingerprint, out var existingBucket))
                {
                    for (var index = existingBucket.Count - 1; index >= 0; index--)
                    {
                        var entry = existingBucket[index];
                        if (entry.ExpiresAt <= now)
                        {
                            existingBucket.RemoveAt(index);
                            count--;
                            continue;
                        }

                        if (entry.Matches(accessControlList))
                        {
                            return entry.Text;
                        }
                    }

                    if (existingBucket.Count is 0)
                    {
                        buckets.Remove(fingerprint);
                        existingBucket = null;
                    }
                }

                if (accessControlList.Count > MaximumCachedAceCount)
                {
                    return text;
                }

                if (count >= Capacity || cachedAceCount > MaximumCachedAceCount - accessControlList.Count)
                {
                    Clear();
                }

                var bucket = existingBucket ?? [];
                buckets[fingerprint] = bucket;
                bucket.Add(new Entry(accessControlList, text, now.Add(Lifetime)));
                count++;
                cachedAceCount += accessControlList.Count;
                return text;
            }
        }

        private void Clear()
        {
            foreach (var bucket in buckets.Values)
            {
                foreach (var entry in bucket)
                {
                    entry.Clear();
                }

                bucket.Clear();
            }

            buckets.Clear();
            count = 0;
            cachedAceCount = 0;
        }

        private readonly record struct AclFingerprint(int Hash, int AceCount)
        {
            public static AclFingerprint Create(AccessControlList accessControlList)
            {
                var hash = new HashCode();
                hash.Add(accessControlList.Owner);
                hash.Add(accessControlList.PrimaryGroupOfOwner);
                hash.Add(accessControlList.Count);
                foreach (var entry in accessControlList.Entries.Span)
                {
                    hash.Add(entry.Identity);
                    hash.Add(entry.Rights);
                    hash.Add(entry.Type);
                    hash.Add(entry.IsInherited);
                    hash.Add(entry.InheritanceFlags);
                    hash.Add(entry.PropagationFlags);
                }

                return new(hash.ToHashCode(), accessControlList.Count);
            }
        }

        private sealed class Entry
        {
            private readonly AccessControlEntry[] entries;
            private readonly SecurityIdentifier? owner;
            private readonly SecurityIdentifier? primaryGroup;

            public Entry(AccessControlList accessControlList, string text, DateTime expiresAt)
            {
                owner = accessControlList.Owner;
                primaryGroup = accessControlList.PrimaryGroupOfOwner;
                entries = accessControlList.Entries.ToArray();
                Text = text;
                ExpiresAt = expiresAt;
            }

            public int AceCount => entries.Length;
            public DateTime ExpiresAt { get; }
            public string Text { get; }

            public bool Matches(AccessControlList accessControlList)
            {
                if (!Equals(owner, accessControlList.Owner) ||
                    !Equals(primaryGroup, accessControlList.PrimaryGroupOfOwner) ||
                    entries.Length != accessControlList.Count)
                {
                    return false;
                }

                var candidateEntries = accessControlList.Entries.Span;
                for (var index = 0; index < entries.Length; index++)
                {
                    if (!entries[index].Equals(candidateEntries[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public void Clear() => Array.Clear(entries);
        }
    }
}
