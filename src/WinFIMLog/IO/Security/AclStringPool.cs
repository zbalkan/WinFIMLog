using System;
using System.Collections.Generic;
using System.Threading;

namespace WinFIMLog.IO.Security
{
    /// <summary>
    ///     Reuses equal ACL payloads without permanently rooting them as <see cref="string.Intern(string)"/>
    ///     would. Baseline members keep the canonical value alive while a snapshot is being built;
    ///     once those members are released, the weak cache is free to release it too.
    /// </summary>
    internal sealed class AclStringPool
    {
        private const int DefaultCapacity = 16_384;

        private readonly Dictionary<int, List<WeakReference<string>>> _buckets = [];
        private readonly int _capacity;
        private readonly Lock _lock = new();
        private int _entryCount;

        public AclStringPool(int capacity = DefaultCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
        }

        public string GetOrAdd(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0)
            {
                return string.Empty;
            }

            var hash = StringComparer.Ordinal.GetHashCode(value);
            lock (_lock)
            {
                if (_buckets.TryGetValue(hash, out var bucket))
                {
                    for (var index = bucket.Count - 1; index >= 0; index--)
                    {
                        if (!bucket[index].TryGetTarget(out var candidate))
                        {
                            bucket.RemoveAt(index);
                            _entryCount--;
                        }
                        else if (string.Equals(candidate, value, StringComparison.Ordinal))
                        {
                            return candidate;
                        }
                    }
                }

                // Keep the pool bounded even when a machine has many genuinely different ACLs.
                // Existing hot entries remain useful; an uncached value is still fully correct.
                if (_entryCount >= _capacity)
                {
                    RemoveDeadEntries();
                    if (_entryCount >= _capacity)
                    {
                        return value;
                    }
                }

                bucket ??= [];
                _buckets[hash] = bucket;
                bucket.Add(new WeakReference<string>(value));
                _entryCount++;
                return value;
            }
        }

        private void RemoveDeadEntries()
        {
            List<int>? emptyBuckets = null;
            foreach (var pair in _buckets)
            {
                var bucket = pair.Value;
                for (var index = bucket.Count - 1; index >= 0; index--)
                {
                    if (!bucket[index].TryGetTarget(out _))
                    {
                        bucket.RemoveAt(index);
                        _entryCount--;
                    }
                }

                if (bucket.Count == 0)
                {
                    (emptyBuckets ??= []).Add(pair.Key);
                }
            }

            if (emptyBuckets is null)
            {
                return;
            }

            foreach (var hash in emptyBuckets)
            {
                _buckets.Remove(hash);
            }
        }
    }
}
