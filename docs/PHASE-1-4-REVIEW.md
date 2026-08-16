# Phase 1–4 completion review

* Review date: 2026-08-16
* Remediation revision: 2026-08-16
* Scope: roadmap Phases 1–4, their decision gates and governance convention
* Overall result: **implementation remediation is complete; formal phase closure
  awaits recorded Windows operational and performance evidence**

A phase closes only after its code, automated tests, repeatable operational
checks and documentation all satisfy the acceptance criteria. This review
therefore distinguishes repository completion from execution of the minimum-host
release gate.

## Decision gates

* **D1 — accepted:** ADR-0001 selects snapshot-first completeness and explicitly
  declines replayable transient-operation history.
* **D2/D3 — accepted:** ADR-0003 defines bounded shedding, acknowledgement,
  failure behaviour, evidence ownership and retention.
* **D4 — accepted:** `docs/DATA-MODEL.md` selects normalised path identity,
  accepts rename/delete-recreate ambiguity and defines explicit old/new path
  payload fields.

## Phase 1 — Implemented; Windows gate pending

### Completed repository evidence

- [x] Filesystem and registry create/change/delete categories map to the
      contracted IDs; discovery/default, service error and explicit health-ID
      routing are also regression-tested.
- [x] ADR-0002 selects all loaded SID hives for HKCU configuration; tests cover
      HKU normalisation, literal HKU entries and exclusion precedence.
- [x] Attribution states are explicit and process lookup failure retains an
      observation with unavailable attribution.
- [x] Configuration syntax is parameterised in validator tests and failed
      initial loading is rejected by the hosted startup validator.
- [x] The `SHKLM` typo and manifest identity are corrected.
- [x] `scripts/phase1-smoke-test.ps1` verifies IDs 7776, 7777, 7778 and 7787.
- [x] The Windows validation workflow runs unit tests and the supported publish;
      the minimum-host workflow runs the operational smoke check and archives
      Event Log evidence.

### Closure evidence still to record

- [ ] Execute the minimum-host workflow on its labelled Windows Server 2019
      runner and retain the successful event export.
- [ ] Record the short-lived registry-writer result from that host. Unit coverage
      exists, but governance requires the operational observation.

## Phase 2 — Implemented; destructive fault evidence pending

### Completed repository evidence

- [x] `IMonitor` is host-token asynchronous. Filesystem and registry sources no
      longer own private cancellation tokens; unexpected ETW termination is
      supervised, reported, reconciled and restarted.
- [x] Filesystem callbacks perform filtering, minimal capture and bounded
      admission only. Hashing, ACL retrieval, attribution waiting, previous-state
      lookup and deduplication run in the enrichment worker.
- [x] Queue-full shedding is bounded, counted and reported as 7791; queue metrics
      appear in heartbeat 7790.
- [x] Watcher errors report affected scope, recreate the watcher and request a
      Tier 0 filesystem snapshot rather than legacy whole-scope discovery.
- [x] Runtime ETW loss polling reports the delta and requests a registry
      snapshot. KCB deletion expires stale handle bindings.
- [x] LiteDB and local Event Log finding writes have bounded retry. Live batches
      return to their buffer on failure and the persistence worker remains
      supervised. Baseline findings remain in a durable local outbox until a
      successful Event Log write.
- [x] Reflection access fails loudly, and CI publishes the declared ReadyToRun,
      non-trimmed, non-AOT, single-file mode.
- [x] `scripts/phase2-fault-injection.ps1` forces bounded queue saturation,
      requires a queue-full gap and verifies post-restart heartbeat recovery.

### Closure evidence still to record

- [ ] Run and archive watcher-overflow, ETW-loss, disk-full and Event Log denial
      scenarios in the disposable minimum-host environment.
- [ ] Complete the supported-host performance table, including callback
      latency, sustained/burst EPS, queue lag and database growth.
- [ ] Record start-up of the published executable on the minimum supported host;
      ordinary hosted CI only proves build/publish compatibility.

## Phase 3 — Implemented; GPO operational gate pending

### Completed repository evidence

- [x] Authoritative machine policy is `HKLM\SOFTWARE\Policies\WinFIMLog`, with
      value-by-value fallback to `HKLM\SOFTWARE\WinFIMLog`. The former
      `HKLM\SOFTWARE\WinFIMLog` preference is a migration-only final fallback.
- [x] ADMX/ADML writes machine policy under the Policies WinFIMLog key.
- [x] Both active keys are mandatory, monitored and protected from exclusions.
- [x] Precedence, policy removal and legacy fallback are unit-tested.
- [x] Invalid runtime reload restores the complete previous in-memory settings
      state rather than publishing a partial scope.
- [x] Canonical `ScopeHash` is order/case/duplicate independent and accompanies
      live findings, health, configuration changes and baseline metadata.
- [x] Effective changes reconfigure filesystem watchers and request immediate
      filesystem and registry Tier 0 snapshots. Registry filtering reads the
      replaced settings matcher for subsequent ETW events.
- [x] Periodic scope reload re-resolves wildcard paths without service restart.

### Closure evidence still to record

- [ ] Run a Windows GPO fixture proving policy override, local-edit resistance,
      policy-removal fallback and rejected protected exclusions.
- [ ] Create a matching user profile and record watcher addition within
      `ScopeReresolutionInterval`.
- [ ] Archive event 7794 and subsequent finding/baseline evidence carrying the
      new scope hash.

## Phase 4 — Implemented; performance and Windows fixtures pending

### Completed repository evidence

- [x] Baseline metadata, membership and reconciliation history are separate.
      Metadata contains source, scope/source identities, schema/algorithm
      versions, lifecycle timestamps/count/status and optional cursors.
- [x] Membership, reconciliation results and the transition to `Complete` share
      one LiteDB transaction. Interrupted and explicitly failed scans cannot be
      queried as complete; lifecycle, duplicate identity, diff and applicability
      tests cover these rules.
- [x] Database deletion causes an immediate startup snapshot without consulting
      `FileDiscoveryCompleted`; the Windows Phase 4 script verifies this.
- [x] Filesystem and registry schedules honour independent configured intervals.
      Watcher loss, ETW loss, service restart and effective scope change request
      the appropriate Tier 0 scan.
- [x] Filesystem source identity uses the volume root, serial number and
      filesystem identity. Registry source identity canonicalises hives and
      includes the loaded SID set for HKCU.
- [x] Filesystem snapshots retain directories, reparse nodes, ACL state, named
      ADS, nullable link count, system/sparse/temporary/offline attributes and
      explicit hash evidence states. Reparse points are not traversed and only
      the unnamed stream is hashed.
- [x] Registry snapshots enumerate subkeys and typed values with ACL/unavailable
      evidence and use the common reconciliation engine for before/after state.
- [x] Cursorless filesystem scans perform a second pass; pass-two evidence is the
      deterministic persistent-state boundary.
- [x] Reconciliation results are durable and emitted as 7795. Failed local Event
      Log delivery increments attempts and remains pending for retry.
- [x] Live rename capture contains explicit `OldPath` and `NewPath`. Snapshot
      path identity deliberately reports delete/create without claiming stable
      continuity.
- [x] Portable tests cover lifecycle/diff/invalidation, directory/file/hash
      evidence, size caps, reparse traversal and identity. Windows CI adds locked
      file, ADS, link-count and typed Registry fixtures.
- [x] `scripts/phase4-snapshot-smoke-test.ps1` verifies a persistent change made
      while stopped and database-loss recovery irrespective of the legacy flag.

### Closure evidence still to record

- [ ] Run the Windows-only ACL, ADS, locked-file, Registry and offline-change
      fixtures on the minimum supported host and archive the results.
- [ ] Record directory ACL mutation, reparse substitution and all unavailable
      hash states in the Windows fixture set.
- [ ] Measure the lowest-spec supported host and replace the provisional
      interval-plus-scan SLA with the approved scan duration, I/O and database
      growth envelope.

## Governance and traceability

- [x] `README.md` now states the default scope, recurring snapshot behaviour,
      notification/attribution limitations and the separately licensed LGPL-2.1
      `NtfsReader` component.
- [x] `docs/LIMITATIONS.md` distinguishes persistent snapshot guarantees from
      transient, downtime, attribution, queue and delivery limitations.
- [x] The defect register contains twenty owned findings with a resolution and
      evidence reference.
- [x] Pull requests have Windows and portable build/test validation.
- [x] A manual minimum-host workflow installs the service, runs Phase 1, 2 and 4
      operational gates and uploads the Event Log export.
- [ ] Formal closure remains prohibited until the pending minimum-host and
      performance artefacts above have been reviewed and retained.
