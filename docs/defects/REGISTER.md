# Defect register

This register records Phase 1 findings carried forward from the architecture review. Status **Resolved** means the named automated test or operational check provides closure; later-phase architectural risks remain open.

| ID | Severity | Finding | Source location | Owner | Status | Resolving change / evidence |
|---|---|---|---|---|---|---|
| P1-01 | High | Filesystem event properties were reversed, defeating IDs 7776–7778 | `BufferConsumer.cs` | Maintainers | Resolved | `EventIdProviderTests` and Phase 1 smoke test |
| P1-02 | High | HKCU configuration did not match ETW `HKEY_USERS\SID` names | `Settings.cs` | Maintainers | Resolved | ADR-0002 and `RegistryScopeMatcherTests` |
| P1-03 | High | Process lookup failure could discard or obscure registry evidence | `RegistryChange.cs` | Maintainers | Resolved | `ProcessAttributionTests.Lookup_failure_returns_unavailable_observation_data` |
| P1-04 | Medium | Attribution had no explicit evidence state | `Change.cs` | Maintainers | Resolved | `AttributionStatus` and `ProcessAttributionTests` |
| P1-05 | Medium | Default SSL key contained `SHKLM` | `Settings.cs`, `README.md` | Maintainers | Resolved | Corrected default and example |
| P1-06 | High | Invalid settings could be silently accepted after load failure | `Settings.cs`, `Program.cs` | Maintainers | Resolved | `ConfigurationValidatorTests` and startup validator |
| P1-07 | Low | Application manifest contained an unrelated, misspelt identity | `app.manifest` | Maintainers | Resolved | Identity is `WinFIMLog.Service` |
| P2-01 | Critical | Watcher callback performed hashing, ACL and database work | `Jobs/FileSystemMonitorJob.cs` | Maintainers | Resolved | `RawFileSystemNotification`, bounded queue and enrichment worker |
| P2-02 | High | Watcher overflow was not observable or recovered | `Jobs/FileSystemMonitorJob.cs` | Maintainers | Resolved | Gap 7791, watcher recreation and Tier 0 snapshot request |
| P2-03 | High | Registry ETW loss was visible only at shutdown | `Jobs/RegistryMonitorJob.cs` | Maintainers | Resolved | Five-second `EventsLost` polling and registry snapshot request |
| P2-04 | High | Registry KCB handle mappings could become stale | `Jobs/RegistryMonitorJob.cs` | Maintainers | Resolved | KCB deletion subscription expires the binding |
| P2-05 | High | Capture admission could grow without a visible bound | `FIM/FileSystemCaptureQueue.cs` | Maintainers | Resolved | ADR-0003 and `FileSystemCaptureQueueTests` |
| P3-01 | High | Preferences and machine policy had no deterministic precedence | `IO/Registry.cs` | Maintainers | Resolved | ADR-0004 and `ConfigurationPrecedenceTests` |
| P3-02 | High | Authoritative configuration could be excluded from monitoring | `Configuration/ScopeIdentity.cs` | Maintainers | Resolved | Mandatory keys and protected-exclusion tests |
| P3-03 | High | Equivalent scope ordering produced unstable identity | `Configuration/ScopeIdentity.cs` | Maintainers | Resolved | Canonical `ScopeHash` and `ScopeIdentityTests` |
| P4-01 | Critical | One-time discovery could not detect persistent downtime changes | `Snapshots/SnapshotService.cs` | Maintainers | Resolved | Startup/periodic Tier 0 snapshots and Phase 4 operational gate |
| P4-02 | High | Baseline membership and live history were conflated | `Data/LiteDbContext.cs` | Maintainers | Resolved | Separate metadata, membership and reconciliation collections |
| P4-03 | High | Incomplete scans could appear authoritative | `Snapshots/BaselineRepository.cs` | Maintainers | Resolved | Atomic completion lifecycle and `BaselineRepositoryTests` |
| P4-04 | High | Directories, reparse points and ADS names lacked distinct evidence | `Snapshots/FileSystemSnapshotSource.cs` | Maintainers | Resolved | ADR-0006 and snapshot evidence tests |
| P4-05 | High | Registry had no recurring before/after state | `Snapshots/RegistrySnapshotSource.cs` | Maintainers | Resolved | Typed recurring registry membership and reconciliation |

## Open architecture findings

The consolidated [architecture remediation roadmap](../ARCHITECTURE-ROADMAP.md)
owns sequencing, dependencies, exit criteria and verification for the current
open findings. The IDs below remain stable when individual changes are linked to
the register.

| ID | Severity | Finding | Owner | Status | Target |
|---|---|---|---|---|---|
| AR-01 | P0 | Tier 0 ignores configured filesystem and registry exclusions | Maintainers | Resolved | M1 scope-pruning tests |
| AR-02 | P0 | Shutdown can strand admitted raw or enriched filesystem events | Maintainers | Resolved | M1 channel-completion test |
| AR-03 | P0 | Watcher lifecycle mutates shared state without one owner | Maintainers | Resolved | M1 serialized watcher lifecycle |
| AR-04 | P0 | Runtime reload exposes mixed mutable configuration generations | Maintainers | Resolved | M1 immutable `EffectiveSettings` publication |
| AR-05 | P1 | Live evidence lacks a durable per-record Event Log outbox | Maintainers | Open | M2 |
| AR-06 | P1 | In-memory aggregation acknowledges evidence before durable delivery | Maintainers | Open | M2 |
| AR-07 | P1 | Snapshot failure has no retry or overdue-baseline objective | Maintainers | Open | M2 |
| AR-08 | P1 | Scanning, recovery and baseline publishing share one serial lane | Maintainers | Open | M3 |
| AR-09 | P1 | Snapshot recovery requests are unbounded and ignore affected scope | Maintainers | Open | M3 |
| AR-10 | P1 | Bulk baseline writes contend with live persistence | Maintainers | Open | M3 |
| AR-11 | P1 | Local baseline and observation storage has no bounded lifecycle | Maintainers | Open | M4 |
| AR-12 | P2 | Cursorless traversal is not a point-in-time filesystem snapshot | Maintainers | Open | M5 |
| AR-13 | P2 | Loaded-hive HKCU scope is not host-wide per-user completeness | Maintainers | Open | M5 |
| AR-14 | P2 | Superseded valid baselines are conflated with failed baselines | Maintainers | Open | M4 |
| AR-15 | P2 | Burst summaries discard entity and attribution distribution | Maintainers | Open | M2 |
