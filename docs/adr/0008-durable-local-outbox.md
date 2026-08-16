# ADR-0008 — Durable local Event Log outbox

* Status: Accepted; supersedes the no-outbox portion of ADR-0007
* Date: 2026-08-16

## Decision

Every structured WinFIMLog record is committed to a LiteDB outbox before it is
acknowledged by a producer. Live latest-state projection changes and their
outbox envelopes share one transaction. A separate publisher retries Event Log
delivery with the stable `RecordId`; delivery is at-least-once and downstream
consumers deduplicate on that ID. Baseline reconciliation results remain a
durable source queue until their envelopes enter the same outbox.

Local burst aggregation is removed. Evidence is forwarded individually and any
rate aggregation belongs to the SIEM after durable hand-off. Successfully
delivered outbox rows have configurable time retention; pending rows are never
removed by retention. There is no automatic dead-letter transition: terminal
sink failures remain pending with attempt count, next-attempt time and last error
until an operator restores the sink or deliberately removes evidence.

## Consequences

Event Log unavailability no longer blocks capture workers or loses already
accepted evidence on restart. It can grow the pending outbox, so heartbeat
records expose pending count and oldest age. Disk/database failure prevents the
producer transaction from acknowledging its batch; bounded admission and gap
reporting remain the degradation boundary. Event Log delivery may be repeated
when a process stops after writing the event but before committing its local
delivery acknowledgement.
