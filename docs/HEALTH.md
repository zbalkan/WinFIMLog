# Health and coverage

Health records use structured log properties so a quiet source is distinguishable
from a blind source.

| ID | Name | Required fields | Emission |
|---|---|---|---|
| 7790 | Heartbeat | time, scope hash, queue depth, oldest age, accepted, processed, dropped, enrichment failures | `HeartbeatInterval` (60 seconds by default) |
| 7791 | Coverage gap | source, scope, scope hash, reason, lost count | Immediately on known loss |
| 7792 | Source recovered | source, scope, scope hash, action | After source recreation |
| 7793 | Sink failure | sink, scope hash, reason, attempt | Each terminal/retry threshold |
| 7794 | Configuration changed | previous scope hash, new scope hash | Once after an effective runtime scope change |
| 7795 | Baseline finding | baseline ID, source, scope hash, change, identity, old/new paths, detection time | Each persistent difference found by reconciliation |

Alert when two expected heartbeat intervals pass, queue age exceeds one interval,
`Dropped` or enrichment failures increase, or a gap is not followed by recovery
and reconciliation. A heartbeat with zero findings means healthy quiet; a missing
heartbeat or unresolved 7791 means coverage is unknown.

The native watcher buffer is 64 KiB, the Windows maximum. This was selected to
absorb short metadata bursts without non-paged-pool growth per watcher; the
supported envelope must be re-measured for each host profile.
