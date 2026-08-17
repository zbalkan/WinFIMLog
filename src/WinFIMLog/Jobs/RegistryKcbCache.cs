using System.Collections.Concurrent;

namespace WinFIMLog.Jobs
{
    internal sealed class RegistryKcbCache
    {
        private readonly ConcurrentDictionary<ulong, string> _entries = new();

        public void Clear() => _entries.Clear();

        public void Remove(ulong handle)
        {
            if (handle != 0)
                _entries.TryRemove(handle, out _);
        }

        public bool TryGet(ulong handle, out string path) =>
            _entries.TryGetValue(handle, out path!);

        public void Update(ulong handle, string path)
        {
            if (handle == 0 || string.IsNullOrEmpty(path))
                return;

            _entries[handle] = path;
        }
    }
}
