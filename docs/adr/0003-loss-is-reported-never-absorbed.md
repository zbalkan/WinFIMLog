# ADR-0003 — Loss is reported, never absorbed

* Status: Accepted
* Date: 2026-08-16

## Admission and acknowledgement

Capture uses a bounded, 8,192-item queue by default. A filesystem notification is
acknowledged when its minimal record is admitted. Full queues shed the new item,
increment `Dropped`, and emit coverage-gap event 7791. This explicit gap is an
acceptable degraded outcome because ADR-0001 makes snapshots authoritative.

Processing acknowledgement occurs only after enrichment has produced an output
item (or determined that the path vanished). Database and Event Log output are
retried by the persistence worker; exhaustion emits event 7793 and retains or
explicitly accounts for the affected batch. Normal process termination stops
sources first and drains admitted output. Forced termination can lose in-memory
records and is therefore a coverage gap bounded by the next snapshot.

## Failure policy

| Failure | Behaviour |
|---|---|
| Queue full | Shed new record; gap 7791; increment `Dropped` |
| Watcher overflow/source failure | Scoped gap; recreate source; request scoped reconciliation |
| Registry ETW loss | Poll every five seconds; emit delta as gap 7791 |
| Disk full/database failure | Exponential retry; do not acknowledge batch; sink failure 7793 |
| Event Log failure | Retry; terminal sink failure is observable |

## Evidence ownership and retention (D3)

The SIEM owns long-term audit history after successful Event Log hand-off. Local
LiteDB owns the latest-state projection and a separate durable outbox. Raw
evidence is not compacted before hand-off. ADR-0008 permits time retention only
for delivered outbox envelopes; pending evidence is retained until delivery.
