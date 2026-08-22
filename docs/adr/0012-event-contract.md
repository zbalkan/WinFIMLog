# ADR-0012 — Structured event contract

* Status: Accepted
* Date: 2026-08-16

## Decision

Filesystem finding fields include `attributionStatus`, `attributionMethod`, `attributionConfidence`, `attributionSourceTimestamp`, `attributionMissingReason`, and `processSequenceNumber`. These fields are optional evidence and do not affect whether a finding is emitted. Their meanings and trust boundaries are defined in [Attribution](0009-attribution-evidence-boundary.md).

Phase 5 records are UTF-8 JSON objects in one of the `WinFIM-Operational`,
`WinFIM-Baseline`, or opt-in `WinFIM-Diagnostic` Windows Event Logs. The
JSON object is the machine-readable Event Log message used by SIEM collectors; it is
not a separate JSON export, file, or storage format. Durable records remain native
LiteDB documents until delivery. The rendered Event Viewer message is **not** the
interface. Every envelope contains:

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
| 7804 | Information | VSS drive-group snapshot creation started |
| 7805 | Information | VSS drive-group snapshot ready |
| 7806 | Error | VSS drive-group snapshot creation failed |
| 7807–7810 | Information | VSS completion and deletion lifecycle |
| 7811 | Error | VSS snapshot-set deletion failed |
| 7812–7814 | Information | VSS root mapping and snapshot scheduling |

VSS lifecycle records carry the snapshot-set identifier when available. Cleanup is attempted for every started snapshot set, including partially failed creation attempts. These events are diagnostics rather than baseline findings.

Filesystem fields are `category`, `operation`, `path`, `oldPath`, `newPath`, `currentHash`,
`previousHash`, `currentSizeBytes`, `previousSizeBytes`, `currentAcl`, `previousAcl`, `objectType`,
`renameCorrelationMethod`, `renameCorrelationConfidence`, `attributionStatus`, `processId`, `processName`,
`userSid`, and `username`. Registry fields are `category`, `key`, `hive`,
`valueName`, `valueData`, `currentAcl`, `previousAcl` and the same attribution fields. Baseline fields are
`baselineId`, `source`, `change`, `identity`, `oldPath`, `newPath`, and
`detectedAt`. Health, gap and configuration records additionally use the fields
defined in [ADR-0013](0013-health-and-coverage-contract.md). Event 7796 remains schema-compatible for
older consumers but local aggregation is no longer emitted.

`operation` is `Created`, `Modified`, or `Deleted`. A same-volume rename or move
reported by `FileSystemWatcher` is `RenamedOrMoved` and carries both `oldPath` and
`newPath` under event 7777. Windows exposes a copied destination only as a creation,
so a copy is truthfully emitted as event 7776/`Created`; no unsupported source-path
claim is inferred. Moves that cross watcher or volume boundaries can likewise appear
as a deletion plus a creation. ACL-only notifications are findings when the captured
ACL differs and carry both current and previously projected ACL evidence when the
local projection is enabled.

`ReadDirectoryChangesW`-style notifications are namespace notifications, not a
signal that the application which made the change has closed its handle. The live
pipeline therefore uses a bounded 75 ms normalization window to fold redundant
`Changed` notifications into a preceding create or rename, then performs enrichment.
It never waits for handle closure, which can be delayed indefinitely. Content reads
open with `FileShare.ReadWrite | FileShare.Delete`, allowing them to coexist with the
DELETE access used by Windows rename operations. A genuinely incompatible sharing
mode can still make hash evidence temporarily unavailable; the finding is retained
with empty evidence rather than delaying or dropping the namespace observation.

The basic watcher removal notification contains only a path. When local projection
is enabled, deletion event 7778 recovers the last observed object type, logical size,
hash, and ACL from that projection; without it, unavailable evidence remains null or
empty rather than being guessed. An add/change for an object that vanishes before
enrichment is still emitted with `Unknown`/unavailable evidence, preserving the
namespace observation despite the unavoidable basic-watcher metadata race.
`ReadDirectoryChangesExW` extended removal metadata and file IDs are not currently
available through the managed `System.IO.FileSystemWatcher` source. On Windows, that
type [calls `ReadDirectoryChangesW`](https://github.com/dotnet/dotnet/blob/c76d92abc5c643acc63895c14640bdc1eb515ef1/src/runtime/src/libraries/System.IO.FileSystem.Watcher/src/System/IO/FileSystemWatcher.Windows.cs#L142-L151)
and [assumes adjacent old/new records in one returned buffer](https://github.com/dotnet/dotnet/blob/c76d92abc5c643acc63895c14640bdc1eb515ef1/src/runtime/src/libraries/System.IO.FileSystem.Watcher/src/System/IO/FileSystemWatcher.Windows.cs#L247-L304)
when it constructs `RenamedEventArgs`. Consequently, a complete pair is explicitly
reported as `RuntimeAdjacentBufferPair` with `Low` confidence, not as file-ID-confirmed
identity. If the runtime surfaces only the old half, WinFIMLog records a deletion; if
it surfaces only the new half, WinFIMLog records a creation. It never invents the
missing path. The similarly named Roslyn language-server type is only an LSP protocol
data contract and is not the filesystem notification implementation.

The [VS Code Windows watcher](https://github.com/microsoft/vscode-filewatcher-windows/blob/main/FileWatcher/FileWatcher.cs#L64-L85)
likewise wraps `System.IO.FileSystemWatcher` and maps a rename to creation and deletion
according to which paths are inside its watched root.
WinFIMLog follows that boundary rule: a move out of effective scope is a deletion, a
move into scope is a creation, and paths outside effective scope are not included in
the event. It retains a qualified rename only when both paths are monitored.

FileWatcherEx adds a [50 ms *quiet-period* processor](https://github.com/d2phap/FileWatcherEx/blob/main/Source/D2Phap.FileWatcherEx/Helpers/EventProcessor.cs#L8-L71)
and [final-state-oriented normalization rules](https://github.com/d2phap/FileWatcherEx/blob/main/Source/D2Phap.FileWatcherEx/Helpers/EventNormalizer.cs#L14-L79)
such as suppressing create-then-delete and merging rename chains. Those rules are
appropriate for UI cache invalidation but not for integrity evidence: WinFIMLog keeps
create/delete and each namespace transition. Its 75 ms window is a fixed upper bound,
not a quiet period that can be extended indefinitely by a busy directory, and only
folds redundant `Changed` noise into the associated observation.

The watcher cannot identify a read as a copy-out operation because reads need not
change directory-listing metadata. It also cannot prove that a newly created file is
a copy merely because its hash matches another file. WinFIMLog therefore reports only
the observable destination creation and makes no copy-source or copy-out claim.

Baseline finding 7795 contains `baselineId`, `source`, `scopeHash`, `change`,
`identity`, `oldPath`, `newPath`, and `detectedAt`. With path identity, a rename
is represented by deterministic deleted and created results rather than an
unsupported claim of stable rename continuity.

## Release-gate smoke check

Run [`scripts/phase1-smoke-test.ps1`](../../scripts/phase1-smoke-test.ps1) from an elevated PowerShell prompt on the minimum supported Windows host after installing and starting the service. The script creates, changes, copies, renames, changes the ACL of, and removes files, then creates, modifies, and removes Registry data under the current user's Run key. It reads matching `WinFIM-Operational` records and fails unless IDs 7776, 7777, 7778, 7786, 7787, and 7788 are observed with the expected complex-operation evidence. The minimum-host workflow runs this operational release gate and retains all dedicated-channel exports.
