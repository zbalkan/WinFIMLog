# Attribution

Attribution is optional evidence attached to an observation. It is never a correctness dependency: disabling either attribution tier does not disable filesystem or registry snapshots, alter a baseline, or suppress a finding.

## Evidence vocabulary

| Evidence | Meaning |
|---|---|
| Process identity | A kernel process instance identified by PID **and** `ProcessSequenceNumber`; PID alone is not an identity. |
| Token subject | The native subject SID/name in a Security audit record. This can describe an impersonating subject where process-level ETW cannot. |
| Inferred user | A user obtained by opening a process token after an event. It is a best-effort inference, not the event's native subject. |
| Unavailable evidence | The source supplied a reference, but rundown, process lifetime, access rights, audit policy, or channel access prevented collection. |

Every filesystem finding carries `attributionStatus`, `attributionMethod`, `attributionConfidence`, `attributionSourceTimestamp`, `attributionMissingReason`, and (when supplied) `processSequenceNumber`. `Attributed` means the event joined a reuse-safe process instance. `Unattributed` means no event correlated. `Unavailable` means referenced evidence could not be retrieved. `RundownMissing` means the mandatory start-of-session process capture did not supply an instance. `ImpersonationAmbiguous` means process identity is known but is insufficient to claim the acting token subject. Findings remain present in every state.

## Kernel ETW tier

The existing kernel session enables process start/stop, process rundown, FileIO, and FileIOInit events. Startup explicitly requests capture state before file correlation is trusted. File activity joins the process index on `(PID, ProcessSequenceNumber)`, so reuse of a numeric PID cannot select a different process instance. Process exit removes only that exact instance.

The source timestamp is the ETW timestamp. Confidence is `High` only for a sequence-number join and `None` for missing evidence. Opening the process token can still fail after exit or through access denial; the kernel instance remains useful while inferred username fields remain empty.

Ordinary kernel process attribution is **not impersonation-safe**. It identifies the process, not necessarily the effective token on the thread which performed an operation. Consumers must not reinterpret a process account as a native subject.

## Optional SACL/Security tier

SACL attribution is disabled by default. Configure a small list of literal `Attribution:Sacl:FileScopes` and/or `RegistryScopes`; wildcards are rejected and the combined list is capped at 64. Deployment owners, rather than WinFIMLog, own applying and restoring SACLs. Broad auditing is expressly unsupported.

At startup the tier calls `AuditQuerySystemPolicy` for the **File System** and **Registry** audit subcategories, then verifies access to the Security channel. A missing policy or access dependency emits a coverage-gap health event and fails service startup visibly. The consumer selects events 4663 and 4657 and preserves their native XML, including native subject and old/new registry evidence where Windows supplies it.

Enabling object-access auditing can materially increase Security-log and forwarding volume. Operators must measure the declared scope, size retention for peak load, restrict Security-log readers, and ensure collection before overwrite. Uninstall or scope removal must restore SACLs and audit policy through the organisation's configuration owner; WinFIMLog deliberately does not mutate either dependency.

Release operators run `scripts/phase6-attribution-check.ps1` after service installation, adding `-SaclTier` when that tier is enabled. This repeatable check verifies the service/provider and makes missing audit policy or Security-channel access a failing release-gate result.

## Explicit non-goal: NTFS `LastUser`

Proposed NTFS `LastUser` attribution is closed as invalid. Application-written document metadata is attacker-controlled and is not identity evidence. It must never populate attribution fields.
