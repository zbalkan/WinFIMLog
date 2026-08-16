# Attribution

Attribution is evidence attached to an observation; it is not a correctness dependency and never decides whether an observation is retained.

| Source | Correlation | Status on success | Missing behaviour |
|---|---|---|---|
| Filesystem | Best-effort path and time correlation between `FileSystemWatcher` and kernel file ETW | `Attributed` | `Unattributed` when no candidate appears in the short correlation window |
| Registry | ETW PID followed by a process/token lookup after the registry event | `Attributed` | `Unavailable` when the process exited, access is denied, or lookup otherwise fails |

`Attributed` means process and available token details were resolved. `Unattributed` means no correlation candidate was found. `Unavailable` means the source supplied an attribution reference but its evidence could not be retrieved. The finding remains present in every case.

## Confidence and limits

Filesystem path/time correlation can select the wrong concurrent operation and is best-effort. Registry PID lookup is post-event: a PID can be reused before lookup. Neither mechanism identifies an impersonated thread's effective subject reliably. `ProcessName`, `Username`, and `UserSID` may therefore be absent and must not be treated as authoritative identity. Consumers should use `AttributionStatus` before interpreting those fields.
