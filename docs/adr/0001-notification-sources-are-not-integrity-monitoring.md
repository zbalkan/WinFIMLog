# ADR-0001 — Notification sources are not integrity monitoring

* Status: Accepted, amended by ADR-0020
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

ADR-0020 scopes that rejection to notification sources. The NTFS change journal
supplies the durable append itself and exposes a resumable position, so it is
admitted as a bounded Tier 0.5 source. It remains subordinate: it is not
completeness authority and does not change the snapshot-first decision above.

## Guarantees and migration

Gaps are explicit and persistent differences are reconciled by the next stable
snapshot. Legacy discovery data is not promoted to completeness evidence and the
legacy completion flag is not authoritative.
