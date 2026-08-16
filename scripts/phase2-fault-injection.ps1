#Requires -RunAsAdministrator
param([string]$ServiceName = 'WinFIMLog', [string]$Scope = "$env:TEMP\WinFIMLogBurst")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Scope | Out-Null
Write-Host 'Generate a watcher/queue burst; confirm event 7791 if capacity is exceeded.'
1..20000 | ForEach-Object { Set-Content -LiteralPath (Join-Path $Scope "$_.txt") -Value $_ }
Start-Sleep 10
Write-Host 'Restart the source and confirm heartbeats resume with QueueDepth returning to zero.'
Restart-Service $ServiceName
Start-Sleep 10
Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='WinFIMLog'; StartTime=(Get-Date).AddMinutes(-10) } |
    Where-Object Id -in 7790,7791,7792,7793 |
    Format-Table TimeCreated, Id, Message -Wrap
Write-Host 'For disk-full and sink-denial tests, run in a disposable VM, fill the data volume or deny Event Log access, then confirm 7793/retry behaviour before restoring access.'
