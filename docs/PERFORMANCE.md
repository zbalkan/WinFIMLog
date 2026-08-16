# Performance qualification

Phase 2 supplies bounded instrumentation rather than claiming an unmeasured host
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

## Supported publish mode

The release mode is .NET 8 `win-x64`, ReadyToRun, non-trimmed and non-AOT.
TraceEvent depends on runtime metadata and validated private parser fields; startup
fails loudly if the tested contract changes. CI must build and start that exact
mode on Windows Server 2019 (build 17763), the minimum supported version.
