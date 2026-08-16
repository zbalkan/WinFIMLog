# ADR-0007 — Event Log as transport

* Status: Accepted
* Date: 2026-08-16

## Decision

Delivery responsibility ends when a versioned record is successfully written to a local
WinFIMLog channel. Downstream acknowledgement is not part of the guarantee, so no
publisher outbox is required. WEF owns forwarding durability and collectors must alert
on missing health records. Findings use a JSON envelope with named fields; schema major
versions are accepted explicitly rather than inferred from rendered text.

Operational, Baseline, and disabled-by-default Diagnostic data use separate logs. The
installer assigns the service SID write access, Administrators and Event Log Readers read
access, bounded sizes, and overwrite-as-needed retention. Wrap, unavailability, retry
exhaustion, and collector outages are accepted risks: sink failures remain queued locally
where applicable, channel wrap is observable through health/collector sequence monitoring,
but a collector-offline wrap can lose events after the local delivery boundary.

## Consequences

Deployment must run `scripts/install-event-channels.ps1` after service creation and before
start. Removal runs `scripts/uninstall-event-channels.ps1`. Sites requiring confirmed
receipt need a future durable, idempotent publisher and a new ADR.
