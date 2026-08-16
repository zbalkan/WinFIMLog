// {{ FIM }} Copyright (C) {{ 2022 }} {{ Zafer Balkan }}
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY

using System;

namespace WinFIMLog.FIM
{
    public interface IChange
    {
        string ACLs { get; set; }

        ChangeCategory ChangeCategory { get; set; }

        ConfigChangeType ConfigChangeType { get; set; }

        DateTime DateTime { get; set; }

        string Entity { get; set; }

        string Id { get; set; }

        string SourceComputer { get; set; }
    }
}