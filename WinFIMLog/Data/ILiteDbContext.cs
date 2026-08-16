// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;
using WinFIMLog.FIM;
using LiteDB;

namespace WinFIMLog.Data
{
    public interface ILiteDbContext : IDisposable
    {
        ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        ILiteCollection<RegistryChange> RegistryChanges { get; }
    }
}