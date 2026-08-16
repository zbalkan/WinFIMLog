# ADR-0006 — Attribute separation

* Status: Accepted
* Date: 2026-08-16

## Decision

Content hash, ACL, named-stream list, node type, link count, and hash state are
independent evidence. An empty digest is never used to imply why content was not
hashed. Hash state distinguishes hashed, size-cap skip, lock, access denial,
vanishing and other failure.

Directories are members and retain ACL evidence. Reparse points are members with
a distinct node type and are not traversed. Named ADS are enumerated, while only
the unnamed `$DATA` stream is hashed. System, sparse, temporary and offline nodes
are retained when enumeration exposes them; their attributes do not silently
exclude the node. Link count is nullable until reuse-safe file identity support
is introduced.
