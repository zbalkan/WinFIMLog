# ADR-0005 — Tier model

* Status: Accepted, amended by ADR-0020
* Date: 2026-08-16

## Decision

Tier 0 is recurring, versioned filesystem and registry snapshot comparison. It
is the authority for eventual detection of persistent differences. A successful
scan has a detection SLA equal to its configured interval plus scan duration.
The initial default is six hours; operators must validate the interval against
the supported-host measurements before release.

Tier 1 notification sources reduce latency and may provide process attribution.
They never advance or complete a Tier 0 baseline, and overlap with a snapshot
does not suppress the snapshot reconciliation result.

Tier 0.5, added by ADR-0020, is the NTFS change journal. It covers create-delete
activity wholly between scans on NTFS volumes with an active journal, publishing
only what Tier 1 did not report. Like Tier 1 it never advances or completes a
Tier 0 baseline. Snapshot-first still does not promise detection of create-delete
activity where Tier 0.5 does not apply: non-NTFS volumes, absent journals,
downtime exceeding journal retention, and records whose path cannot be resolved.

Cursorless filesystem scans require two consecutive complete observations to agree.
A scope that does not converge within the bounded pass limit is invalid and retried.
Interrupted, unstable or failed scans are never comparison inputs.
