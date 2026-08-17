// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinFIMLog.FIM
{
    public interface IBuffer<T>
        where T : IChange
    {
        Task Add(T change);

        Task AddRange(IEnumerable<T> changes);

        public int Count();

        public bool HasNext();

        public List<T> Take(int count);

        public List<T> TakeAll();
    }
}
