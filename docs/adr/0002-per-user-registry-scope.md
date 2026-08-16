# ADR-0002 — Per-user registry scope

* **Status:** Accepted
* **Date:** 2026-08-16

## Context

Kernel registry ETW reports a loaded user hive as `HKEY_USERS\<SID>`, while administrators commonly express per-user policy as `HKEY_CURRENT_USER`. Treating those names literally made the configured Run keys ineffective for most ETW observations.

## Decision

`HKEY_CURRENT_USER` means the corresponding path in **every loaded user SID hive**. WinFIMLog normalises an ETW name of `HKEY_USERS\<SID>\<path>` to `HKEY_CURRENT_USER\<path>` for matching only. A SID component must begin `S-1-`; `.DEFAULT` and other non-SID hives are not implied. The event retains its original `HKEY_USERS\<SID>` entity.

An explicitly configured `HKEY_USERS` path remains literal. It selects only that path or SID and is not rewritten. Exclusions use the same rules and take precedence over inclusions.

## Consequences

The effective HKCU scope changes as user hives load and unload. The service observes loaded hives only and does not load offline profiles. Each Registry baseline records its concrete resolved SID-root manifest; a logon or logoff starts a distinct lineage and cannot appear as mass value creation or deletion. Operators requiring one particular account configure its explicit `HKEY_USERS\<SID>` path.
