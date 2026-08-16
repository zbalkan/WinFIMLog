# SIEM integration

## Deployment and delivery boundary

Create the three secured channels after creating the service by running
`scripts/install-event-channels.ps1` elevated. WinFIMLog guarantees successful local
write only, as decided in [ADR-0007](adr/0007-event-log-as-transport.md). WEF queueing,
collector availability, and forwarded-log retention are operated downstream. Alert if
event 7790 is missing for more than twice the configured heartbeat interval and monitor
Event Log service/channel errors.

Use a source-initiated WEF subscription selecting `WinFIMLog-Operational` and
`WinFIMLog-Baseline`. Diagnostic forwarding is opt-in. A minimal XPath selector is:

```xml
<QueryList><Query Id="0"><Select Path="WinFIMLog-Operational">*</Select>
<Select Path="WinFIMLog-Baseline">*</Select></Query></QueryList>
```

Configure the subscription for normal delivery (or minimise latency where attribution
latency is operationally important), Kerberos mutual authentication, and a collector
whose computer account is authorised by policy. Validate channel ACLs using
`wevtutil gl WinFIMLog-Operational` and test both an Event Log Readers member and an
unprivileged account.

## Parsing and queries

Parse the event message as JSON, check `schemaVersion == 1`, then dispatch on
`recordType`; never apply a regular expression to prose. Unknown fields in version 1
are ignored. An example Kusto query is:

```kusto
Event
| where Source startswith "WinFIMLog-"
| extend record=parse_json(RenderedDescription)
| where toint(record.schemaVersion) == 1
| project TimeGenerated, record.recordType, record.recordId,
          record.scopeHash, record.fields
```

Parser fixtures are exercised by `EventContractTests`: filesystem, registry, baseline,
gap, health, configuration, and aggregation discriminators must all parse without regex.

## Operational checks

After a clean install, verify all three logs exist, their `channelAccess` contains the
service SID and expected reader groups, and their maximum sizes match policy. Stop the
collector to exercise WEF queueing; fill a small test channel to exercise wrap; stop the
Event Log service in an isolated test VM to verify retries and retained admitted records.
Remove the service, run `scripts/uninstall-event-channels.ps1`, and verify the logs and
sources no longer exist.
