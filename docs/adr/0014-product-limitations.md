# ADR-0014 — Explicit product limitations

* Status: Accepted, amended by ADR-0020
* Date: 2026-08-16

## Decision

WinFIMLog is snapshot-first. Recurring snapshots provide eventual detection of
persistent state differences; notification sources provide lower latency and
best-effort attribution. The following limitations remain explicit.

## Completeness and evidence

* Activity which creates and deletes an object entirely between snapshots may
  be absent if the notification source also misses it. Tier 0.5 (ADR-0020)
  replays the journal across the windows where the notification source is known
  to have missed; it remains uncovered on non-NTFS volumes, where the journal is
  inactive or has already discarded the window, and where the record's path
  cannot be resolved. WinFIMLog does not own a replayable audit history.
* Service downtime and boot activity have no live notification coverage.
  Persistent filesystem or registry state left by that activity is detected by
  the next complete snapshot. Tier 0.5 replays the downtime window from a
  persisted cursor where the journal still retains it; a downtime longer than
  journal retention is a reported gap, and registry activity is not covered.
* Tier 0.5 findings carry no content hash, no ACL evidence and no attribution
  when the object no longer exists. They are namespace evidence, marked
  `observationSource: UsnJournal`, and a record whose parent directory was also
  deleted carries `pathUnresolved` and an unmatched placeholder path. A replay
  can also re-report an operation Tier 1 already reported at a gap boundary;
  consumers deduplicate these by `RecordId` as they do any other retry.
* A failed or interrupted snapshot is retained as `Building` or `Invalid` and
  is never completeness evidence. Detection is delayed until a later successful
  snapshot.
* Cursorless filesystem capture proves consecutive agreement, not a transactional
  point-in-time volume image. A scope that continues changing is rejected and
  retried rather than published as complete.
* Path is the current entity identity. Rename and delete/recreate continuity is
  therefore not authoritative; snapshot comparison reports path creation and
  deletion, while live rename evidence may carry old/new paths.
* Named ADS are enumerated, but only the unnamed `$DATA` stream is hashed.
  Reparse points are recorded as nodes and deliberately not traversed.

## Notification sources

* `FileSystemWatcher` can overflow. WinFIMLog reports the affected scope,
  recreates the watcher and requests reconciliation, but operations within the
  gap are not a replayable history.
* Registry ETW can lose events under load. Runtime loss counters produce a gap
  event and a registry snapshot request; attribution for the lost operations is
  unrecoverable.
* HKCU means every currently loaded SID hive. Offline profiles are not mounted;
  the concrete loaded-hive manifest is recorded in baseline lineage so logon and
  logoff do not masquerade as Registry deletion or creation.
* Admission uses a bounded in-memory queue. Queue-full shedding and forced
  process termination are explicit gaps bounded, for persistent changes, by the
  next complete snapshot.

## Attribution

* Filesystem attribution is best-effort path/time correlation and can select a
  concurrent operation or remain unattributed.
* Registry attribution resolves a PID after the event. Process exit, PID reuse
  and access denial can make it unavailable or ambiguous.
* Ordinary kernel process attribution is not impersonation-safe and must not be
  interpreted as an authoritative token subject.

## Delivery and retention

* Local Event Log hand-off is the current delivery boundary. Downstream SIEM
  receipt is not acknowledged by WinFIMLog.
* Accepted live observations and their stable envelopes share a durable outbox
  transaction. Event Log retry is at-least-once, so consumers deduplicate by
  `RecordId`. A process kill can still lose notifications not yet admitted.
* Delivered outbox envelopes and complete baseline generations use bounded,
  configurable retention. Pending outbox evidence is never time-expired. A
  prolonged sink outage can therefore consume local disk; pending count and age
  are health signals and operators must alert before capacity is exhausted.
