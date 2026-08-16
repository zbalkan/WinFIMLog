#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$expected = 'WinFIMLog-Operational','WinFIMLog-Baseline','WinFIMLog-Diagnostic'
foreach ($name in $expected) {
    $configuration = & wevtutil.exe gl $name
    if ($LASTEXITCODE -or $configuration -notmatch 'enabled: true') { throw "$name is unavailable" }
    if ($configuration -notmatch 'channelAccess:.*S-1-5-80-') { throw "$name does not grant the service SID access" }
}
Write-Host 'Phase 5 channel installation and service-SID ACL checks passed.'
