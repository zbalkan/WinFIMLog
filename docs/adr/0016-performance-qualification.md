# ADR-0016 — Performance qualification gate

* Status: Accepted, amended by ADR-0020
* Date: 2026-08-16

## Decision

## Snapshot release gate

The initial snapshot interval is 21,600 seconds (six hours). Until Windows
measurements are recorded for the lowest supported host, this is a provisional
persistent-change detection SLA of **interval plus successful scan duration**,
not a certified host envelope. Record item count, elapsed time, peak working
set, bytes read/written and database growth for both snapshot sources. Scan
duration must remain below the configured interval; otherwise reduce scope or
increase the interval and record the approved value.

The service supplies bounded instrumentation rather than claiming an unmeasured host
envelope. Release qualification must record Windows version, CPU, memory, storage,
scope item count and watcher count, then capture:

* discovery wall-clock time and database growth;
* callback p50/p95/p99 latency (callbacks only filter, copy and attempt admission);
* sustained EPS for 30 minutes and a 60-second burst EPS;
* maximum queue depth, oldest-item age, drops and enrichment failures;
* database and Event Log write latency/failure counts.

Run `scripts/phase2-fault-injection.ps1` in an elevated Windows test VM. Attach
the resulting Event Log export and performance table to the release gate. A
profile is supported only when it has no unexplained gaps, no unbounded growth,
and queue lag returns to zero after the burst.

## Tier 0.5 journal gate

The NTFS change journal source (ADR-0020) ships opt-in and stays opt-in until its own
measurements are recorded. It is event-driven rather than polled, so the profile to record is the
cost of one replay of a representative coverage gap, plus confirmation that steady state issues no
journal read at all. ADR-0020 lists the measurements and the pass condition.

## Supported publish mode

The release mode is .NET 8 `win-x64`, ReadyToRun, non-trimmed and non-AOT.
TraceEvent depends on runtime metadata and validated private parser fields; startup
fails loudly if the tested contract changes. CI must build and start that exact
mode on Windows Server 2019 (build 17763), the minimum supported version.

## Complexity analysis

The **latest-state projection** uses a persisted, invariant-normalized entity key and a unique LiteDB index. A normal projection update performs an expected constant-time indexed lookup and replacement for each reduced entity, after the linear, in-memory batch reduction. The versioned schema marker makes the legacy normalization and case-duplicate removal a one-time operation: migration scans each projection linearly, keeps one winner per normalized identity, and writes both projection migrations plus the marker transactionally. Later process starts validate only the two unique indexes rather than re-running full projection scans.

Registry-root reconciliation first orders distinct configured roots, then uses a case-insensitive hash set to test only segment-delimited ancestors. Its work is therefore dominated by sorting plus the number of path segments examined, rather than comparing every root with every other root. Registry snapshot traversal keeps only lightweight subkey-path work items pending; it opens and disposes each `RegistryKey` as it is processed, bounding live registry handles independently of subtree depth and sibling count.

Filesystem snapshot traversal streams each directory's entries and pushes yielded child paths to an explicit work stack. This removes per-directory child-array allocation and preserves children already yielded when an enumerator fails late. The pending-path stack can still grow with the unprocessed traversal frontier, so peak managed memory must be measured on wide trees. Watcher reconfiguration uses case-insensitive hash sets and dictionaries, giving expected linear reconciliation in the number of desired paths and active watchers, apart from operating-system watcher create/dispose cost.

The remaining runtime priorities are measured I/O rather than collection scans: NTFS enumeration, content hashing, ACL and Registry reads, LiteDB and Event Log latency, and the size of the filesystem traversal frontier. Legacy discovery persists paths through a configured maximum degree of parallelism, defaulting to two workers; qualification must record the configured concurrency alongside path count and elapsed time so storage saturation and queue effects remain visible.
