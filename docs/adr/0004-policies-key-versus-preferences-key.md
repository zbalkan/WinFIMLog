# ADR-0004: Policies key versus preferences key

* Status: Accepted
* Date: 2026-08-16

## Decision

Machine policy at `HKLM\SOFTWARE\Policies\WinFIMLog` is authoritative on a value-by-value basis. When a policy value exists it replaces the same preference at `HKLM\SOFTWARE\WinFIMLog`; deleting it immediately exposes the preference (or the built-in default). The preference key remains available during migration. Both keys are always monitored and neither may be excluded.

A canonical SHA-256 `ScopeHash` covers sorted, case-normalised, de-duplicated effective paths, exclusions, extensions and registry keys. It changes only with effective scope. The service periodically re-reads configuration, resolves wildcard paths, adjusts watchers, and emits old/new hashes.

## Consequences

GPO refresh may restore policy after local deletion. Policy values are not copied to preferences: this avoids registry tattooing. Removing or disabling a GPO therefore follows normal Group Policy deletion semantics and reveals the locally owned preference. Existing deployments require no immediate preference migration, but administrators should move managed values into policy and retain preferences only as deliberate fallback.
