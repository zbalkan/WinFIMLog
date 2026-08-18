#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$expected = 'WinFIMLog'
foreach ($name in $expected) {
    $configuration = & wevtutil.exe gl $name
    if ($LASTEXITCODE -or $configuration -notmatch 'enabled: true') { throw "$name is unavailable" }
    if ($configuration -notmatch 'channelAccess:.*S-1-5-80-') { throw "$name does not grant the service SID access" }
    $access = ($configuration | Select-String 'channelAccess:').ToString()
    if ($access -match '\(A;;0x2;;;WD\)' -or $access -match '\(A;;0x2;;;BU\)') {
        throw "$name grants write access to an unauthorised broad identity"
    }
}

$heartbeat = Get-WinEvent -FilterHashtable @{ LogName='WinFIMLog'; ProviderName='WinFIMLog'; Id=7790 } -MaxEvents 1
$record = $heartbeat.Message | ConvertFrom-Json
if ($record.schemaVersion -ne 1 -or $record.recordType -ne 'Health') { throw 'Heartbeat is not a version-1 structured Health record.' }
foreach ($field in 'queueDepth','oldestItemAgeMs','accepted','processed','dropped','enrichmentFailures') {
    if ($null -eq $record.fields.$field) { throw "Heartbeat field '$field' is absent." }
}
Write-Host 'Phase 5 WinFIMLog Event Log and service-SID ACL checks passed.'
