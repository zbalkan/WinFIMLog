# ADR-0005 — Tier model

* Status: Accepted
* Date: 2026-08-16

## Decision

Tier 0 is recurring, versioned filesystem and registry snapshot comparison. It
is the authority for eventual detection of persistent differences. A successful
scan has a detection SLA equal to its configured interval plus scan duration.
The initial default is six hours; operators must validate the interval against
the supported-host measurements before release.

Tier 1 notification sources reduce latency and may provide process attribution.
They never advance or complete a Tier 0 baseline, and overlap with a snapshot
does not suppress the snapshot reconciliation result. Snapshot-first therefore
does not promise detection of create-delete activity wholly between scans.

Cursorless filesystem scans use a second pass. The later observation wins,
making overlap deterministic. Interrupted or failed scans are invalid and are
never comparison inputs.
