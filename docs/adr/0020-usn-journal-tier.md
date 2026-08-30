# ADR-0020 — NTFS change journal replay as a Tier 0.5 source

* Status: Accepted
* Date: 2026-08-30
* Amends: ADR-0001, ADR-0005, ADR-0014, ADR-0016

## Context

ADR-0014 states the limitation precisely:

> Activity which creates and deletes an object entirely between snapshots may be absent **if the
> notification source also misses it.**

Tier 0 structurally cannot see such activity, because the object is gone by scan time. Tier 1
normally does see it: `FileSystemWatcher` reports the create and the delete while it is healthy.
The gap therefore opens only where Tier 1 misses, and those windows are already detected and
reported: watcher failure or overflow (`FileSystemMonitorJob`), capture-queue shedding
(`FileSystemCaptureQueue`), and the service downtime before start.

For a security-driven deployment that gap matters — staging a payload and removing it during a
watcher overflow is exactly what an attacker would want to be invisible.

ADR-0001 rejected a hybrid durable journal because it "would require durable append and source
cursors before acknowledging every source event, which neither `FileSystemWatcher` nor the current
registry ETW source can supply end to end." That reasoning holds for those sources and does not
extend to the NTFS change journal: NTFS provides the durable append as part of the transaction,
and the journal exposes a position that can be persisted and resumed.

## Decision

The change journal is admitted as **Tier 0.5**: a filesystem-only source that replays the journal
across windows where Tier 1 coverage was lost, and is otherwise inert.

* Replay is **event-driven, not polled**. It runs on service start and on the coverage gaps the
  health contract already reports. With no gap reported, no volume handle is opened and no journal
  read occurs.
* A continuously-polled journal was rejected. Because Tier 1 covers transient activity while it is
  healthy, polling would reproduce Tier 1's stream and then need a correlation index to discard it
  — machinery larger than the source it serves, for coverage already held.
* Tier 0.5 **never** advances, completes or invalidates a Tier 0 baseline, and never suppresses a
  snapshot reconciliation result. Tier 0 remains the sole completeness authority.
* Journal findings carry **no content hash, no ACL evidence and no attribution** once the object is
  gone. They are namespace evidence, marked `observationSource: UsnJournal`, and must not be read
  as equivalent to snapshot or watcher evidence.
* A replay may re-report an operation Tier 1 also reported, at the boundary of a gap. **This
  duplicate is accepted.** Consumers already deduplicate by `RecordId` under ADR-0008, and
  suppressing it costs more than it saves.
* Cursor invalidation — a recreated journal, or a position trimmed out of the retained ring — is a
  coverage gap under ADR-0003: reported through `IHealthReporter`, not absorbed. Loss is not
  persisted to the database; the Event Log is the delivery boundary under ADR-0007.
* A record whose parent directory was itself deleted cannot be resolved to a path, so it cannot be
  scope-matched. It is published with `pathUnresolved` set rather than dropped, because discarding
  it would discard the activity this source exists to observe.
* A single replay is capped. Reaching the cap is itself reported as a gap.
* The source is **opt-in** (`EnableUsnJournalMonitoring`, default `0`) until the gate below is
  recorded.

## Consequences for the amended records

* **ADR-0001** — the rejection of a hybrid durable journal is scoped to notification sources. The
  change journal is admitted as a bounded exception which is still not completeness authority.
* **ADR-0005** — the tier model gains Tier 0.5, which covers lost-coverage windows on NTFS volumes.
* **ADR-0014** — the create-delete and downtime limitations are narrowed to where replay does not
  reach. A new limitation is added: Tier 0.5 findings carry no hash, ACL or attribution.
* **ADR-0016** — a qualification gate is added below.

## Qualification gate

Replay reads a whole volume's journal for the window, filtering to scope afterwards, so its cost
scales with volume write activity during the gap rather than with the size of the monitored scope.

* Wall-clock duration and records read for a replay of a representative gap, and the proportion
  discarded by scope filtering.
* Peak working set during replay, and path-resolution failures as a share of records.
* Confirmation that steady state is genuinely idle: no handle opened and no read issued while no
  gap is reported.
* Frequency with which the replay record cap is reached on the busiest supported volume.

A profile is supported when a replay of the worst observed gap completes well inside the snapshot
interval and steady-state idle is confirmed.
