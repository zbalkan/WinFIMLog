# Data model

## Identity decision (D4)

Phase 4 uses case-insensitive, normalised absolute **path identity**. Rename is
represented explicitly by `OldPath` and `NewPath` in reconciliation results,
but path identity cannot prove rename continuity: a rename appears as deletion
plus creation, and delete/recreate at one path is ambiguous. Reuse-safe file ID
is deferred. Registry values use their canonical hive/key/value path.

## Baseline lifecycle

`BaselineMetadata` records an ID, source, scope hash, volume/hive identity,
schema and algorithm versions, timestamps, count, optional cursors, and status.
A scan moves `Building` → `Reconciling` → `Complete`. Failure or cancellation
makes it `Invalid`. Only an applicable `Complete` baseline can be a comparison
input. Changing database, scope, source identity, schema or algorithm starts a
new lineage and invalidates incompatible metadata.

Membership is held separately from historical live observations. Members and
reconciliation results are committed in the same LiteDB transaction as the
metadata transition to `Complete`; consequently an interrupted commit cannot
publish a partial complete baseline.

The obsolete `FileDiscoveryCompleted` registry value is neither read nor used
as a snapshot gate. A missing/deleted database therefore causes an immediate
startup baseline.

## Evidence

Filesystem membership separates node type, SHA-256 content, hash state, ACL
evidence/state, named streams, and nullable link count. Directories and reparse
points are evidence nodes; reparse points are not traversed. Registry membership
supports typed raw value data and unavailable states. See ADR-0006 for attribute
semantics.

## Migration

Legacy `fileSystemChanges` and `registryChanges` remain historical observations.
They are not silently promoted to a complete baseline. The first Phase 4 scan
builds the new baseline collections; the old discovery flag remains only so an
older binary can be rolled back safely.
