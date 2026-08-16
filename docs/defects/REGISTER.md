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
