# Current limitations

WinFIMLog is snapshot-first. Recurring snapshots provide eventual detection of
persistent state differences; notification sources provide lower latency and
best-effort attribution. The following limitations remain explicit.

## Completeness and evidence

* Activity which creates and deletes an object entirely between snapshots may
  be absent if the notification source also misses it. WinFIMLog does not own a
  replayable audit history.
* Service downtime and boot activity have no live notification coverage.
  Persistent filesystem or registry state left by that activity is detected by
  the next complete snapshot; transient activity is not guaranteed.
* A failed or interrupted snapshot is retained as `Building` or `Invalid` and
  is never completeness evidence. Detection is delayed until a later successful
  snapshot.
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
* In-memory live observations can be lost on forced termination. Known queue or
  source loss is reported; an ungraceful process kill cannot emit its own gap.
* Local time-based retention is deliberately disabled until raw evidence and
  latest-state projection ownership are separated. Operators own Event Log and
  SIEM retention according to ADR-0003.
