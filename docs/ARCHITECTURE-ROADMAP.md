# Architecture remediation roadmap

## Purpose

This roadmap consolidates the repository-wide architecture reviews into one
ordered delivery plan. It deliberately separates defects that violate the
current contract from longer-term improvements to the contract. A phase is not
complete merely because code was merged: its exit criteria, automated tests and
Windows operational evidence must all pass.

## Architectural target

WinFIMLog remains snapshot-first: Tier 0 provides eventual detection of
persistent state and Tier 1 notifications reduce latency. The repaired design
must preserve four end-to-end invariants:

1. One immutable configuration generation defines notification filtering,
   snapshot membership and the `ScopeHash` attached to their evidence.
2. Every admitted notification reaches an explicit terminal state: durably
   queued, delivered, or reported lost.
3. A `Complete` baseline has a stated consistency boundary and contains exactly
   the effective scope for its configuration generation.
4. Recovery, delivery and storage remain bounded under source failure, sink
   failure and long-running scans.

## Priorities and dependencies

| ID | Priority | Problem | Milestone | Status |
|---|---|---|---|---|
| AR-01 | P0 | Tier 0 ignores filesystem and registry exclusions | M1 | Complete |
| AR-02 | P0 | Shutdown can strand raw or enriched filesystem events | M1 | Complete |
| AR-03 | P0 | Watcher lifecycle has multiple unsynchronised owners | M1 | Complete |
| AR-04 | P0 | Mutable settings publish mixed configuration generations | M1 | Complete |
| AR-05 | P1 | Live persistence and Event Log delivery have no durable per-record hand-off | M2 | Open |
| AR-06 | P1 | In-memory burst aggregation can acknowledge evidence before Event Log delivery | M2 | Open |
| AR-07 | P1 | Snapshot failure advances the normal interval without a recovery objective | M2 | Open |
| AR-08 | P1 | Scans, recovery requests and baseline outbox publishing share one serial lane | M3 | Open |
| AR-09 | P1 | Snapshot requests are unbounded and affected scope is not actionable | M3 | Open |
| AR-10 | P1 | Full baseline writes contend with latency-sensitive live persistence | M3 | Open |
| AR-11 | P1 | Baseline and observation storage have no bounded lifecycle | M4 | Open |
| AR-12 | P2 | Cursorless filesystem traversal is not a point-in-time snapshot | M5 | Open |
| AR-13 | P2 | Loaded-hive-only HKCU coverage is not host-wide per-user completeness | M5 | Open |
| AR-14 | P2 | Superseded valid baselines are conflated with failed baselines | M4 | Open |
| AR-15 | P2 | Burst grouping loses entity and attribution distribution | M2 | Open |

P0 means the current behavior contradicts the stated scope or reliability
contract and blocks a trustworthy release. P1 is required for resilient
production operation. P2 changes or sharpens the product guarantee and may be
delivered after the immediate correctness work.

## M1 — Restore scope and pipeline correctness

**Status: Complete (2026-08-16).** M1 introduced immutable effective-setting
generations, canonical snapshot predicates, serialized watcher ownership and a
completion-driven shutdown pipeline. Its regression evidence is the solution
build plus filesystem scope-pruning, registry scope-pruning and capture-channel
completion tests. Registry capture tests require Windows and remain part of the
Windows release gate when run on a non-Windows development host.

### M1.1 Publish one immutable effective configuration (AR-04)

Replace the mutable settings property set with an immutable
`EffectiveSettings` generation. Resolve registry values into locals, validate
them, build matchers and `ScopeHash`, and publish one reference atomically. Each
callback, enrichment operation, monitor reconfiguration and snapshot captures
that reference once and uses it for its entire operation.

The generation must include monitored and excluded filesystem paths, excluded
extensions, monitored and excluded registry keys, compiled matchers, hashing
limits, intervals, queue limits and registry-monitor enablement. Changing
registry enablement must start or stop the registry source through the same
coordinated lifecycle as filesystem reconfiguration.

**Exit criteria**

* Concurrent readers observe either generation A or generation B, never a mix.
* Findings and baselines carry the generation's `ScopeHash` and relevant
  generation identifier.
* A rejected candidate leaves the old generation and all its sources active.

### M1.2 Make one canonical scope predicate authoritative (AR-01)

Snapshot sources must consume the same generation and canonical predicates as
notification admission. Filesystem capture checks the predicate before adding a
node and before descending; an excluded directory prunes its entire subtree.
Registry capture applies the registry matcher before recording a key or value
and before recursion, while retaining HKCU-to-loaded-HKU expansion semantics.

**Exit criteria**

* Excluded paths, extensions, keys and subtrees cannot appear in a baseline.
* Snapshot membership and notification admission agree for an identical
  configuration generation.
* `ScopeHash` identifies the scope actually traversed, not merely configured
  input that capture later ignores.
* Tests prove that excluded directories and keys are not traversed, rather than
  merely removed after expensive enumeration.

### M1.3 Give watcher lifecycle one owner (AR-03)

Route start, stop, reconfigure and error recovery through a single owner task or
command loop. It exclusively owns the watcher collection and a lifecycle state.
Callbacks report failures to the owner; they do not remove, dispose or recreate
watchers themselves. Recovery verifies that the failed scope still belongs to
the current configuration generation.

A lock is acceptable as an interim containment change only if every collection
access and lifecycle flag uses it, callbacks cannot recreate during shutdown,
and event handlers are detached before disposal. Health reporting and snapshot
requests must occur outside the critical section.

**Exit criteria**

* Stop, reconfigure and error recovery cannot interleave collection mutations.
* A removed scope cannot be resurrected by a late error callback.
* Disposal is idempotent and a normal stop cannot fail with collection-modified
  errors.

### M1.4 Complete the admitted-work pipeline before stopping consumers (AR-02)

Replace cancellation-only shutdown with completion propagation:

1. Stop notification producers.
2. Complete the raw capture writer.
3. Let enrichment read until the raw channel is empty and completed.
4. Signal completion of the enriched stream.
5. Drain and persist all enriched filesystem and registry records.
6. Flush durable publishers and stop sinks last.

Hosted-service registration order alone is not the protocol. The orchestrator
must await each boundary and report any item that cannot reach a terminal state.

**Exit criteria**

* Controlled shutdown accounts for every admitted raw record.
* Persistence cannot finish before enrichment completion.
* Shutdown timeouts emit a coverage gap with remaining counts.
* A host-level test stops the service during active enrichment and observes no
  unexplained difference between admitted and terminal metrics.

## M2 — Establish a real delivery boundary

### M2.1 Use one durable outbox for all evidence (AR-05)

Persist each live observation and its stable event envelope atomically with a
delivery state. A publisher independently writes pending records to Event Log
and marks them delivered only after the real writer succeeds. Document delivery
as at-least-once and require downstream deduplication by stable record ID.
Baseline findings should use the same abstraction rather than a separate
delivery mechanism.

Do not return an entire partially delivered batch to an undifferentiated buffer.
Track attempts and terminal state per record, expose oldest pending age, and
provide an explicit dead-letter or operator-retry policy.

### M2.2 Move or durably model aggregation (AR-06, AR-15)

Preferred: forward individual security evidence and aggregate in the SIEM. If
local aggregation remains, persist buckets before acknowledging their input and
group by a forensically meaningful key. Summaries must include distinct-entity
count, bounded first/last samples and attribution distribution, not only the
last record ID.

### M2.3 Give failed snapshots a recovery objective (AR-07)

Return explicit scan outcomes and schedule bounded exponential retry separately
from the periodic cadence. Emit a coverage gap when no applicable baseline is
current or baseline age exceeds its SLA; emit recovery only after successful
completion. Heartbeats expose last successful completion, scan status, duration,
attempt count and age.

**M2 exit criteria**

* A crash after outbox commit cannot erase an accepted finding.
* A crash before aggregation flush cannot erase acknowledged evidence.
* Replaying a partially delivered batch preserves stable IDs.
* A transient snapshot failure retries before the full normal interval.
* Tests cover sink failure at every record boundary and restart recovery.

## M3 — Isolate scheduling, recovery and storage workloads

### M3.1 Split the snapshot service (AR-08, AR-09)

Separate periodic scheduling, reconciliation coordination, source-specific scan
execution and outbox publication. Maintain at most one coalesced pending request
per source and resolved scope, with reason aggregation and priority escalation.
Make affected scope actionable where the source can safely reconcile it;
otherwise explicitly promote it to a full-source scan.

Filesystem and registry scans may proceed independently under a shared resource
budget. Baseline outbox publishing must continue while either scan is running.

### M3.2 Protect live admission from bulk baseline writes (AR-10)

Give storage mutation one prioritized actor, or adopt a store supporting the two
workload classes. Stage baseline members in bounded chunks and use a small atomic
promotion transaction for membership, reconciliation results and `Complete`
metadata. Reserve write capacity and latency budgets for the live outbox.

**M3 exit criteria**

* Recovery request memory is bounded during a scan storm.
* A filesystem scan cannot block registry recovery or outbox delivery.
* Baseline construction cannot push live admission beyond its supported queue
  latency under the qualified host profile.
* Fault-injection results record scan, delivery and live-write latency together.

## M4 — Make persistence sustainable and lineage honest

### M4.1 Separate storage ownership and retention (AR-11)

Define distinct stores or collections for the latest-state projection, baseline
manifests/membership, durable delivery outbox and optional historical evidence.
After successful comparison and delivery, retain only the baseline generations
needed for the next diff unless an explicit local-history policy says otherwise.
Add capacity, free-space, growth and compaction health signals.

### M4.2 Separate validity from applicability (AR-14)

Model lifecycle (`Building`, `Complete`, `Failed`) independently from
applicability (`Current`, `Superseded`). A complete baseline valid for an older
configuration remains historical evidence and can participate in a deliberate
rollback lineage; only failed or inconsistent scans are invalid.

**M4 exit criteria**

* A steady-state retention test demonstrates bounded database growth.
* Disk exhaustion has a documented admission and recovery policy.
* Configuration changes preserve old valid lineage without selecting it as the
  current comparison input.
* Projection, outbox and historical-evidence retention can be configured and
  reasoned about independently.

## M5 — Strengthen or narrow the completeness guarantee

### M5.1 Define a filesystem consistency boundary (AR-12)

The current two-pass traversal must not be described as a point-in-time
snapshot. Select and record one of these contracts:

1. scan an NTFS VSS snapshot;
2. anchor traversal and reconciliation to USN journal cursors with explicit
   journal-wrap behavior; or
3. retain both cursorless passes, treat disagreement as indeterminate and retry
   until the documented convergence rule is met.

Stable file identity should be introduced where available so rename and
delete/recreate can be distinguished more accurately. If the portable fallback
remains path-based, its weaker guarantee must be explicit in every baseline.

### M5.2 Resolve per-user registry scope (AR-13)

Choose whether the product guarantees active-user coverage or host-wide
per-user coverage. For active-user coverage, version the resolved loaded-hive
manifest, report hive entry/exit and never interpret hive unload as mass value
deletion. For host-wide coverage, enumerate profiles and mount offline hives
through a controlled, read-only mechanism.

**M5 exit criteria**

* Every completed baseline states its consistency method and start/end boundary.
* Tests mutate early- and late-enumerated objects during capture and verify the
  selected convergence contract.
* The resolved HKCU hive set is evidence, and logoff cannot create false mass
  deletion findings.
* Documentation and health records use the selected completeness guarantee
  consistently.

## Verification matrix

| Risk | Required automated or operational evidence |
|---|---|
| Scope divergence | Filesystem and registry integration tests compare notification predicates with snapshot membership, including subtree pruning |
| Mixed configuration | Concurrent reload stress test proves every captured view is entirely generation A or B |
| Watcher races | Deterministic tests interleave error, stop and reconfigure, including removal during recovery |
| Shutdown loss | Host test stops during active enrichment and reconciles admitted, completed, failed, persisted and delivered counts |
| Delivery ambiguity | Restart and fault-injection tests fail each outbox boundary and verify stable-ID replay |
| Snapshot recovery | Source failures prove retry, overdue gap, successful recovery and heartbeat state transitions |
| Workload contention | Windows qualification measures queue age and Event Log/database latency during full scans |
| Unbounded growth | Multi-generation retention test demonstrates the configured storage ceiling |
| Cursorless inconsistency | Mutation-during-traversal test validates the chosen VSS, journal or convergence contract |
| Dynamic HKCU | Logon/logoff and offline-profile tests validate the selected per-user guarantee |

## Release gates

* **Correctness gate:** M1 is complete before the next production release.
* **Reliability gate:** M2 is complete before claiming durable local hand-off or
  graceful-shutdown drain guarantees.
* **Scale gate:** M3 and M4 are complete before publishing a supported sustained
  EPS, scope-size or long-running storage envelope.
* **Completeness gate:** M5 is complete before describing filesystem baselines as
  point-in-time snapshots or HKCU monitoring as host-wide per-user coverage.
