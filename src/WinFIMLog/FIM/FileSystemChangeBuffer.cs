// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinFIMLog.FIM
{
    public class FileSystemChangeBuffer : IBuffer<FileSystemChange>
    {
        private readonly ConcurrentDictionary<string, FileSystemChange> store = new();

        public Task Add(FileSystemChange change)
        {
            ArgumentNullException.ThrowIfNull(change);
            store[change.Id] = change;
            return Task.CompletedTask;
        }

        public Task AddRange(IEnumerable<FileSystemChange> changes)
        {
            ArgumentNullException.ThrowIfNull(changes);

            // These batches are small and ConcurrentDictionary already synchronizes writes.
            // Avoid Parallel.ForEach's partitioning, work items, and delegates on this hot path.
            foreach (var change in changes)
                store[change.Id] = change;

            return Task.CompletedTask;
        }

        public int Count() => store.Count;

        public bool HasNext() => !store.IsEmpty;

        public List<FileSystemChange> Take(int count)
        {
            var result = new List<FileSystemChange>(Math.Min(count, store.Count));
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

        public List<FileSystemChange> TakeAll()
        {
            var result = new List<FileSystemChange>(store.Count);
            foreach (var item in store)
            {
                store.TryRemove(item.Key, out var message);
                if (message != null) { result.Add(message); }
            }

            return result;
        }
    }
}
