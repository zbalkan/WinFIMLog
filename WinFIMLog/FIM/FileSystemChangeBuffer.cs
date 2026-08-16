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
            store.AddOrUpdate(change.Id, change, (_, _) => change);
            return Task.CompletedTask;
        }

        public Task AddRange(IEnumerable<FileSystemChange> changes)
        {
            ArgumentNullException.ThrowIfNull(changes);

            Parallel.ForEach(changes, change => store.AddOrUpdate(change.Id, change, (_, _) => change));

            return Task.CompletedTask;
        }

        public int Count() => store.Count;

        public bool HasNext() => !store.IsEmpty;

        public List<FileSystemChange> Take(int count)
        {
            var result = new List<FileSystemChange>();
            var counter = 0;
            foreach (var key in store.Keys)
            {
                if (counter == count)
                {
                    break;
                }

                store.TryRemove(key, out var message);
                if (message != null) { result.Add(message); }

                counter++;
            }
            return result;
        }

        public List<FileSystemChange> TakeAll()
        {
            var result = new List<FileSystemChange>();
            foreach (var key in store.Keys)
            {
                store.TryRemove(key, out var message);
                if (message != null) { result.Add(message); }
            }

            return result;
        }
    }
}