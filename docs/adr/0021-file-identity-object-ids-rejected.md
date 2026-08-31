# ADR-0021 — NTFS Object IDs and file reference numbers rejected as entity identity

* Status: Accepted
* Date: 2026-08-31
* Amends: ADR-0011 (D4)

## Context

ADR-0011 left this open: "Reuse-safe file ID is deferred." Tier 0.5 (ADR-0020) subsequently
introduced the first real use of an NTFS-native identifier — `FileReferenceNumber`, from USN
records — which made the deferred question concrete: should file reference numbers, or NTFS
Object IDs, replace or supplement path identity for filesystem entities?

Both were evaluated against their actual Win32 contracts before deciding, not against how they
are commonly assumed to behave.

## Decision

**Neither is adopted as entity identity.** Path identity (ADR-0011, D4) stands unchanged as the
sole identity for filesystem baseline membership, reconciliation, and event correlation.

### NTFS Object IDs — rejected outright

- **Not universal.** An Object ID exists only where something explicitly called
  `FSCTL_CREATE_OR_GET_OBJECT_ID` or `FSCTL_SET_OBJECT_ID`. WinFIMLog discovers files passively
  and creates none. Most monitored objects have no Object ID at all; building identity on a field
  the majority of entities lack is not viable for a general baseline.
- **Breaks on the exact operation that matters most.** The Object ID tracks the MFT record, not
  "the file" as a user or an analyst means it. The standard safe-save pattern — write a temp file,
  rename the original aside, rename the temp into place — leaves the Object ID attached to the
  discarded backup, while the new content at the canonical path carries a different or absent
  Object ID. This is also the shape of an atomic malicious payload swap. An identity model that
  loses continuity across exactly the operation a FIM needs to see through is worse than no
  continuity claim at all.
- **Would not have resolved ADR-0011's actual gap.** ADR-0011 flags delete/recreate-at-one-path
  ambiguity. Object IDs do not disambiguate that case either, for the reason above.

### `FileReferenceNumber` (the 64-bit MFT reference already in use) — scoped, not adopted

Tier 0.5 already uses this value, but only as a **transient resolution key**, never as persisted
identity, and this ADR makes that boundary explicit rather than incidental:

- It is volume-scoped and reused: `FileReferenceNumber` is a 48-bit MFT record index plus a
  16-bit sequence number. Deleting an object frees its MFT record for reuse by an unrelated,
  later object; the sequence number distinguishes old from new only within its own 16-bit space.
  A value that outlives the record it named is possible, not merely theoretical.
- `DirectoryPathCache` (Tier 0.5) caches parent-directory paths keyed by this value, with no TTL —
  only a clear-on-overflow backstop. This is an accepted, bounded risk for a resolution cache
  whose failure mode is a stale *path label on a namespace-evidence finding*, not a bounded risk
  for a persisted identity, which this value must never become.
- `GetFinalPathNameByHandle`, used to turn a resolved handle back into a path, returns exactly one
  name when the target has multiple hard links, chosen arbitrarily by the API, not by us. A
  journal-sourced finding's path is therefore not guaranteed to be the name an operator or
  `MonitoredPaths` scoping would recognize as canonical.

## Consequences

- ADR-0011's "Reuse-safe file ID is deferred" is resolved, not merely left open: the deferral
  stands, on the evidence above, not for lack of investigation.
- `docs/adr/0014-product-limitations.md` gains the two `FileReferenceNumber`-derived limitations
  (hard-link path selection, MFT-record-reuse cache staleness) as explicit Tier 0.5 limitations.
- `FileReferenceNumber` must not be added to any persisted model as an identity or correlation
  key. Its only sanctioned use is as an ephemeral parameter to `OpenFileById` inside
  `DirectoryPathCache`, for the lifetime of one resolution.
- If a future need for rename-continuity identity resurfaces, this ADR is the record that Object
  IDs and file reference numbers were both evaluated and rejected for that purpose — a new
  proposal must argue against the specific failure modes above, not merely note that NTFS exposes
  an identifier.

## Related, separately tracked

Investigating `OpenFileById`'s real parameter contract during this evaluation surfaced that
`NativeMethods.FileIdFull`, the struct WinFIMLog currently passes to it, does not match Win32's
`FILE_ID_DESCRIPTOR` (missing the `dwSize` and `Type` discriminator fields). That is an
implementation defect in the Tier 0.5 path-resolution call, not a consequence of this identity
decision, and is tracked as a roadmap item rather than recorded here.
