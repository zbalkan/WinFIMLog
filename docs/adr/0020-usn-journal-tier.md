# ADR-0020 — NTFS change journal as a Tier 0.5 source

* Status: Accepted
* Date: 2026-08-30
* Amends: ADR-0001, ADR-0005, ADR-0014, ADR-0016

## Context

ADR-0005 and ADR-0014 state that snapshot-first does not promise detection of activity that
creates and deletes an object entirely between scans. That limitation is structural for Tier 0:
the object is gone by scan time, so no snapshot can observe it. It is not covered by Tier 1
either, because a `FileSystemWatcher` notification for it is lost outright when the watcher
overflows or the service is not running.

For a security-driven deployment that class of activity — staging a payload and removing it —
is exactly what monitoring is for.

ADR-0001 rejected a hybrid durable journal, on the grounds that it "would require durable append
and source cursors before acknowledging every source event, which neither `FileSystemWatcher` nor
the current registry ETW source can supply end to end." That reasoning holds for those sources
and does not extend to the NTFS change journal: NTFS itself provides the durable append as part
of the transaction, and the journal exposes a position that can be persisted and resumed.

## Decision

The NTFS change journal is admitted as **Tier 0.5**, a filesystem-only source subordinate to
Tier 0 and secondary to Tier 1.

* It **never** advances, completes or invalidates a Tier 0 baseline, and never suppresses a
  snapshot reconciliation result. Tier 0 remains the sole completeness authority.
* It publishes a record only when no `FileSystemWatcher` observation for the same normalized path
  and category was admitted inside a correlation window. Tier 1 wins every duplicate, because
  Tier 1 observations carry process attribution and journal records carry none.
* Journal findings carry **no content hash, no ACL evidence and no attribution** when the object
  no longer exists. They are namespace evidence, marked `observationSource: UsnJournal`, and must
  not be read as equivalent to snapshot or watcher evidence.
* Cursor invalidation — journal recreation, or a cursor trimmed out of the retained ring — is a
  coverage gap under ADR-0003: it is persisted, reported, and triggers a Tier 0 snapshot request
  rather than being absorbed.
* A record whose parent directory was itself deleted cannot be resolved to a path and therefore
  cannot be scope-matched. Such records are published with `pathUnresolved` set rather than
  dropped, because discarding them would discard the activity this source exists to observe.
* The source is **opt-in** (`EnableUsnJournalMonitoring`, default `0`) until the ADR-0016
  qualification below is recorded.

## Consequences for the amended records

* **ADR-0001** — the rejection of a hybrid durable journal is scoped to notification sources.
  The change journal is admitted as a bounded exception which is still not completeness authority.
* **ADR-0005** — the tier model gains Tier 0.5. The statement that snapshot-first does not promise
  detection of create-delete activity between scans now holds only where Tier 0.5 does not apply:
  non-NTFS volumes, volumes with no active journal, downtime exceeding journal retention, and
  records whose path cannot be resolved.
* **ADR-0014** — the create-delete and downtime limitations are narrowed to those same cases. A
  new limitation is added: Tier 0.5 findings carry no hash, ACL or attribution.
* **ADR-0016** — a qualification gate is added below.

## Qualification gate

Tier 0.5 stays opt-in until these are recorded for the lowest supported host, because the journal
is read per volume rather than per monitored path: the cost scales with total volume write
activity, not with the size of the monitored scope.

* Sustained and burst records read per second, and the proportion discarded by scope filtering.
* CPU and working set during a write storm of Windows Update scale, per volume.
* Path-resolution cache hit rate and eviction rate.
* Correlation suppression rate. A persistently low rate means Tier 1 is lossier than assumed and
  is a finding in itself; a persistently high one means Tier 0.5 is mostly redundant on that host.
* Added database growth from cursor and gap records.
* End-to-end latency from the filesystem operation to Event Log emission, including the settle
  delay that gives Tier 1 first claim.

A profile is supported only when scope-filtered reads keep pace with the configured poll interval
and the correlation tracker does not reach its capacity backstop.
