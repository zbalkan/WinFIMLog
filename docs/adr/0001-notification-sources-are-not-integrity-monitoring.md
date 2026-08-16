# ADR-0001 — Notification sources are not integrity monitoring

* Status: Accepted
* Date: 2026-08-16

## Decision

WinFIMLog is **snapshot-first**. Versioned recurring snapshots are the
authority for eventual detection of persistent state differences. Notifications
reduce detection latency and may provide attribution, but are never proof of
completeness. WinFIMLog does not promise a replayable audit history of every
transient operation.

The rejected alternative was a hybrid durable journal. It would require durable
append and source cursors before acknowledging every source event, which neither
`FileSystemWatcher` nor the current registry ETW source can supply end to end.

## Guarantees and migration

Gaps are explicit and persistent differences are reconciled by the next stable
snapshot. Legacy discovery data is not promoted to completeness evidence and the
legacy completion flag is not authoritative.
