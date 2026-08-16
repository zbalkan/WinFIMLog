# Configuration

WinFIMLog reads machine policy from `HKLM\SOFTWARE\Policies\WinFIMLog`, then falls back **per value** to preferences at `HKLM\SOFTWARE\WinFIMLog`. Policy never gets written by the service. Defaults are created only in preferences. Both locations are mandatory monitored scope and cannot be excluded.

| Value | Type | Default | Validation and effect |
|---|---|---|---|
| `MonitoredPaths` | `REG_MULTI_SZ` | Windows/program/user security paths | Absolute paths; `*` only as a complete segment. Creates recursive watchers. |
| `ExcludedPaths` | `REG_MULTI_SZ` | volatile Windows paths | Same path syntax; removes matching paths. |
| `ExcludedExtensions` | `REG_MULTI_SZ` | `.log`, `.evtx`, `.etl`, `.wal`, `.db-wal`, `.db` | Literal, case-insensitive suffixes. |
| `MonitoredKeys` | `REG_MULTI_SZ` | security-sensitive keys | Full hive names, without wildcards. Both configuration keys are added unconditionally. |
| `ExcludedKeys` | `REG_MULTI_SZ` | empty | Full hive names, without wildcards; cannot cover either configuration key. |
| `EnableRegistryMonitoring` | `REG_DWORD` | `1` | `1` enables ETW registry monitoring. |
| `EnableLocalDatabase` | `REG_DWORD` | `1` | `1` persists observations locally. |
| `HashLimitMB` | `REG_DWORD` | `1024` | Maximum file size considered for hashing. |
| `HeartbeatInterval` | `REG_DWORD` | `60` | Seconds; `0` disables heartbeat. |
| `CaptureQueueCapacity` | `REG_DWORD` | `8192` | Must exceed zero. |
| `WatcherBufferSizeKB` | `REG_DWORD` | `64` | 8–64 KiB. |
| `ScopeReresolutionInterval` | `REG_DWORD` | `300` | At least 10 seconds; controls policy refresh and wildcard re-resolution. |

`ScopeHash` is lower-case SHA-256 over canonical effective scope. Ordering, case and duplicate entries do not affect it. A changed hash is included in configuration event 7794 and subsequent findings and heartbeats. Invalid initial configuration prevents startup; invalid runtime configuration is rejected, leaves the last valid scope active, and emits a configuration coverage-gap diagnostic.
