# ADR-0012 — Structured event contract

* Status: Accepted
* Date: 2026-08-16

## Decision

Filesystem finding fields include `attributionStatus`, `attributionMethod`, `attributionConfidence`, `attributionSourceTimestamp`, `attributionMissingReason`, and `processSequenceNumber`. These fields are optional evidence and do not affect whether a finding is emitted. Their meanings and trust boundaries are defined in [Attribution](0009-attribution-evidence-boundary.md).

Phase 5 records are UTF-8 JSON objects in one of the `WinFIMLog-Operational`,
`WinFIMLog-Baseline`, or opt-in `WinFIMLog-Diagnostic` Windows Event Logs. The
rendered Event Viewer message is **not** the interface. Every envelope contains:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | yes | Contract major version; currently `1` |
| `eventId` | integer | yes | Stable allocation below |
| `recordType` | string | yes | Discriminator for the fields object |
| `occurredAt` | RFC 3339 timestamp | yes | UTC emission time |
| `recordId` | string | yes | Stable finding/observation identifier |
| `scopeHash` | string | yes | Canonical effective scope identity |
| `fields` | object | yes | Record-specific named fields; null means unavailable |
| `channel` | string enum | yes | `Operational`, `Baseline`, or `Diagnostic` |

Consumers **must** accept schema version 1, ignore unknown fields within version 1,
and reject an unknown major version explicitly. Field names and meanings are additive
within a major version; existing fields will not change type or meaning.

The WinFIMLog channels use the following stable ID allocation.

| ID | Level | Meaning |
|---:|---|---|
| 7770 | Error | Service or monitoring error |
| 7776 | Information | Filesystem object created |
| 7777 | Information | Filesystem object changed |
| 7778 | Information | Filesystem object deleted |
| 7780 | Information | Other service event, including heartbeat and lifecycle |
| 7786 | Information | Registry key or value created |
| 7787 | Information | Registry key or value changed |
| 7788 | Information | Registry key or value deleted |
| 7790 | Information | Health heartbeat and queue counters |
| 7791 | Error | Scoped coverage gap |
| 7792 | Information | Monitoring source recovered |
| 7793 | Error | Database or Event Log sink failure |
| 7794 | Warning | Effective configuration changed (previous/new scope hashes) |
| 7795 | Warning | Tier 0 baseline reconciliation finding |
| 7796 | Information | Reserved legacy burst aggregation summary (not emitted) |
| 7797 | Information | Optional native Security-audit attribution evidence |

Filesystem fields are `category`, `path`, `oldPath`, `newPath`, `currentHash`,
`previousHash`, `objectType`, `attributionStatus`, `processId`, `processName`,
`userSid`, and `username`. Registry fields are `category`, `key`, `hive`,
`valueName`, `valueData` and the same attribution fields. Baseline fields are
`baselineId`, `source`, `change`, `identity`, `oldPath`, `newPath`, and
`detectedAt`. Health, gap and configuration records additionally use the fields
defined in [ADR-0013](0013-health-and-coverage-contract.md). Event 7796 remains schema-compatible for
older consumers but local aggregation is no longer emitted.

Baseline finding 7795 contains `baselineId`, `source`, `scopeHash`, `change`,
`identity`, `oldPath`, `newPath`, and `detectedAt`. With path identity, a rename
is represented by deterministic deleted and created results rather than an
unsupported claim of stable rename continuity.

## Release-gate smoke check

Run [`scripts/phase1-smoke-test.ps1`](../../scripts/phase1-smoke-test.ps1) from an elevated PowerShell prompt on the minimum supported Windows host after installing and starting the service. The script creates, changes, and removes a file, writes a value under the current user's Run key, then reads matching `WinFIMLog-Operational` records. It fails unless IDs 7776, 7777, 7778, and 7787 are observed. The minimum-host workflow runs this operational release gate and retains all dedicated-channel exports.
