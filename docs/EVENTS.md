# Event ID allocation

The **WinFIMLog** Application Event Log source uses the following stable Phase 1 allocation.

| ID | Level | Meaning |
|---:|---|---|
| 7770 | Error | Service or monitoring error |
| 7776 | Information | Filesystem object created |
| 7777 | Information | Filesystem object changed |
| 7778 | Information | Filesystem object deleted |
| 7780 | Information | Other service event, including heartbeat and lifecycle |
| 7786 | Information | Registry key or value created |
| 7787 | Information | Registry key or value changed |
| 7788 | Information | Registry key or value deleted |
| 7790 | Information | Health heartbeat and queue counters |
| 7791 | Error | Scoped coverage gap |
| 7792 | Information | Monitoring source recovered |
| 7793 | Error | Database or Event Log sink failure |

A finding binds `changeType` to `FileSystem` or `Registry` and `category` to `Created`, `Changed`, or `Deleted`. The pair determines the ID. Rendered message text is not an authoritative interface.

## Release-gate smoke check

Run [`scripts/phase1-smoke-test.ps1`](../scripts/phase1-smoke-test.ps1) from an elevated PowerShell prompt on the minimum supported Windows host after installing and starting the service. The script creates, changes, and removes a file, writes a value under the current user's Run key, then exports matching Application log records. It fails unless IDs 7776, 7777, 7778, and 7787 are observed. This operational check is a required release gate until it runs in Windows CI.
