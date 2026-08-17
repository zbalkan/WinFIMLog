using System;
using System.Collections.Generic;
using System.Threading;

namespace WinFIMLog.Jobs
{
    internal sealed class RegistryKcbCache
    {
        private const int DefaultCapacity = 16_384;

        private readonly int _capacity;
        private readonly Dictionary<ulong, string> _entries = [];
        private readonly Lock _lock = new();

        public RegistryKcbCache(int capacity = DefaultCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        public void Remove(ulong handle)
        {
            if (handle != 0)
            {
                lock (_lock)
                {
                    _entries.Remove(handle);
                }
            }
        }

        public bool TryGet(ulong handle, out string path)
        {
            lock (_lock)
            {
                return _entries.TryGetValue(handle, out path!);
            }
        }

        public void Update(ulong handle, string path)
        {
            if (handle == 0 || string.IsNullOrEmpty(path))
            {
                return;
            }

            lock (_lock)
            {
                if (!_entries.ContainsKey(handle) && _entries.Count >= _capacity)
                {
                    // KCB delete events can be lost. Bound retained mappings and evict one
                    // conservatively rather than allowing the cache to grow for process lifetime.
                    ulong evictedHandle;
                    using (var entries = _entries.GetEnumerator())
                    {
                        entries.MoveNext();
                        evictedHandle = entries.Current.Key;
                    }

                    _entries.Remove(evictedHandle);
                }

                _entries[handle] = path;
            }
        }
    }
}
