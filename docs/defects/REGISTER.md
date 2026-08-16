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
