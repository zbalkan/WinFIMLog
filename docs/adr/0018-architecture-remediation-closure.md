# ADR-0018 — Architecture remediation closure

* Status: Accepted
* Date: 2026-08-16

## Context

Repository-wide review identified fifteen architecture risks across scope correctness,
shutdown, configuration publication, delivery, scheduling, persistence, snapshot
consistency, per-user Registry scope, lineage and aggregation. The stable IDs and
final evidence are retained in ADR-0019.

## Decision

The remediation program is complete. The resulting architecture has these boundaries:

1. `EffectiveSettings` publishes one immutable configuration generation used by
   notification admission, enrichment, watcher lifecycle and snapshots.
2. Filesystem and Registry snapshots use the same exclusions as live monitoring.
3. Producer completion drains raw capture, enrichment, projection and the durable
   outbox in order; timeout loss is an explicit coverage gap.
4. Every finding enters a durable, stable-ID Event Log outbox. Local aggregation is
   removed and downstream consumers deduplicate at-least-once delivery.
5. Filesystem and Registry snapshot schedulers, recovery requests and baseline finding
   publication are independent and bounded. Failed or overdue scans are observable.
6. Latest-state projections, delivered outbox rows and complete baseline generations
   have separate retention ownership.
7. Cursorless filesystem baselines complete only after two consecutive observations
   agree. A non-convergent scan is invalid and retried.
8. HKCU baselines record the concrete loaded-SID root manifest. Hive-set changes start
   a new lineage instead of reporting unload as mass deletion.
9. Baseline validity and applicability are separate: failed scans are `Invalid`, while
   valid historical lineages are `Complete` and `Superseded`.

## Release gates

A release requires a warning-free solution build, the automated regression suite,
Markdown link validation, and the elevated Windows operational checks defined by
ADR-0012 and ADR-0016. Windows-only Registry, ADS, file-lock, Event Log, ETW and service
lifecycle checks cannot be replaced by portable unit tests.

## Consequences

There is no continuing roadmap document. Future architecture work is proposed as a new
ADR and tracked by its implementation issue rather than by reopening this closure.
