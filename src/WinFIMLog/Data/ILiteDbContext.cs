// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;
using LiteDB;
using WinFIMLog.Events;
using WinFIMLog.FIM;
using WinFIMLog.Snapshots;

namespace WinFIMLog.Data
{
    public interface ILiteDbContext : IDisposable
    {
        ILiteCollection<BaselineMember> BaselineMembers { get; }
        ILiteCollection<BaselineMetadata> Baselines { get; }
        ILiteCollection<EventOutboxRecord> EventOutbox { get; }
        ILiteCollection<FileSystemChange> FileSystemChanges { get; }

        ILiteCollection<ReconciliationResult> ReconciliationResults { get; }
        ILiteCollection<RegistryChange> RegistryChanges { get; }

        bool ExecuteTransaction(Action action);
    }
}
