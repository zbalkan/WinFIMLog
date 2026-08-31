# Roadmap: Tier 0.5 hardening and closure

* Status: Draft
* Date: 2026-08-31
* Branch: `claude/usn-logs-fim-comparison-9vwtc9`

## Context

Tier 0.5 (ADR-0020) is implemented as episodic journal replay and merged onto this branch. It has
never executed against a real NTFS volume: every result to date comes from unit tests against
pure logic and P/Invoke layout assertions, not a live `DeviceIoControl` call. That gap, plus a
side investigation into NTFS file-identity mechanisms (Object IDs, `FileReferenceNumber`), found
one confirmed defect and two undocumented limitations before a single elevated Windows run.

That is the reason ADRs are first below, not last: two of the three findings are decisions
(what identity model is correct, what limitations are real) that should be recorded before the
code that depends on them is touched again, and the third is a defect whose fix should be
verified against the decision, not around it.

## 1. New ADRs — done, this session

- **ADR-0021** (new): NTFS Object IDs and `FileReferenceNumber` evaluated and rejected as entity
  identity. Resolves ADR-0011's open "reuse-safe file ID is deferred" with rationale, so the
  question does not get re-litigated the next time someone reaches for `OpenFileById`.
- **ADR-0011** amended: points to ADR-0021 for why the deferral stands.
- **ADR-0014** amended: two new Tier 0.5 limitations recorded — hard-link path selection is
  arbitrary (not WinFIMLog's choice), and `DirectoryPathCache` has no TTL against MFT record
  reuse. Both scoped explicitly to path-label risk on namespace findings, not to Tier 0 identity
  or evidence, which ADR-0021 keeps untouched.

No code changed in this step. Everything after this point should be read against these three
documents, not against assumptions the earlier Tier 0.5 work carried.

## 2. Fix: `OpenFileById` parameter layout — done, this session

`NativeMethods.FileIdFull` is replaced by `FileIdDescriptor`, matching Win32's real 24-byte
`{ dwSize; Type; union { LARGE_INTEGER; GUID; FILE_ID_128; }; }`. The bug was worse on inspection
than first estimated: the original struct's `LowPart`/`HighPart` naming borrowed
`LARGE_INTEGER`'s alternate 32+32 view, but declared both fields as 8-byte (`ulong`/`long`)
rather than 4-byte, so it neither carried the required discriminator nor reconstructed the
64-bit value correctly even by accident. `DirectoryPathCache.Resolve` now sets
`Type = FileId` (the plain 64-bit member, per ADR-0021 — never `ObjectId`) and `Size` from
`Marshal.SizeOf`, and reinterprets the `ulong` file reference into the field's bit pattern
directly rather than splitting it.

`FileIdDescriptorTests` pins the struct's total size (24) and field offsets via
`Marshal.OffsetOf`, in the same style as `UsnRecordParserTests.Record_header_field_offsets_match_the_win32_layout`,
plus a round-trip check for a file reference with its top bit set. 195 tests passing (191 + 4 new).

## 3. Windows validation (carried over, unstarted)

Everything in the original Tier 0.5 plan's verification steps 4–6 remains outstanding and unrun:

- Force a watcher overflow (`scripts/phase2-fault-injection.ps1`); confirm exactly one replay for
  the affected scope, findings carrying `observationSource: UsnJournal` and a `usn`, and that a
  create-delete pair inside the overflow window is reported.
- Stop the service, create and delete a file, restart; confirm the start-up replay reports it, and
  that a second restart with no activity replays nothing.
- Confirm steady-state idle: with no coverage gap reported, no volume handle opens and no journal
  read occurs.
- New, from this session's findings: exercise a monitored directory with an additional hard link,
  and confirm the path returned in a Tier 0.5 finding is exactly the one ADR-0014 now documents as
  arbitrary — not a silent mismatch against `MonitoredPaths` that drops the finding.

This step depends on item 2: running it against the unfixed `OpenFileById` call would validate a
broken path, not the feature.

## 4. ADR-0020 qualification gate

Once validation (3) passes, record the measurements ADR-0020 requires before the default can move
off opt-in: replay duration and records read for a representative gap, peak working set, confirmed
steady-state idle, and cap-hit frequency on the busiest supported volume. `EnableUsnJournalMonitoring`
stays `0` until this is recorded, per ADR-0016's release gate.

## 5. Deferred, unchanged

- **Windows SACL audit trail**: still deferred until Tier 0.5 lands and is measured, per the
  earlier decision to sequence rather than couple these two features. Not reopened by this
  session's findings; revisit after item 4.

## 6. Process note, not an action item

This session found and fixed three real defects (`USN_RECORD_V2` and `READ_USN_JOURNAL_DATA`
field widths; `FILE_ID_DESCRIPTOR`, item 2) in code that was written, unit-tested, and committed
without ever running against real Windows or NTFS. All three were structurally undetectable by
the test suite as it existed at the time each was introduced — they surfaced only from reading the
Win32 contracts directly against the code: twice chasing a build failure, once for an unrelated
question about file GUIDs. Worth naming plainly: nothing about this repository's CI currently
executes Tier 0.5 code on Windows, so a fourth such defect is exactly as likely to be sitting in
`UsnJournalReader` right now as the first three were before they were found. Item 3 is the only
thing that closes that risk; no amount of additional unit testing on the current CI matrix can.
