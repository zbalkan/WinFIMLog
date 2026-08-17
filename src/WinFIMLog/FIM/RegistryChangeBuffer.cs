using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinFIMLog.FIM
{
    public class RegistryChangeBuffer : IBuffer<RegistryChange>
    {
        private readonly ConcurrentDictionary<string, RegistryChange> store = new();

        public Task Add(RegistryChange change)
        {
            ArgumentNullException.ThrowIfNull(change);

            store[change.Id] = change;
            return Task.CompletedTask;
        }

        public Task AddRange(IEnumerable<RegistryChange> changes)
        {
            ArgumentNullException.ThrowIfNull(changes);

            // These batches are small and ConcurrentDictionary already synchronizes writes.
            // Avoid Parallel.ForEach's partitioning, work items, and delegates on this hot path.
            foreach (var change in changes)
            {
                store[change.Id] = change;
            }

            return Task.CompletedTask;
        }

        public int Count() => store.Count;

        public bool HasNext() => !store.IsEmpty;

        public List<RegistryChange> Take(int count)
        {
            var result = new List<RegistryChange>(Math.Min(count, store.Count));
            var counter = 0;
            foreach (var item in store)
            {
                if (counter == count)
                {
                    break;
                }
                store.TryRemove(item.Key, out var message);
                if (message != null) { result.Add(message); }

                counter++;
            }

            return result;
        }

        public List<RegistryChange> TakeAll()
        {
            var result = new List<RegistryChange>(store.Count);
            foreach (var item in store)
            {
                store.TryRemove(item.Key, out var message);
                if (message != null) { result.Add(message); }
            }

            return result;
        }
    }
}
