#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$source = 'WinFIMLog'
$start = Get-Date
$configured = (Get-ItemProperty 'HKLM:\SOFTWARE\FIM' -Name MonitoredPaths).MonitoredPaths |`
  ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) } |`
  Where-Object { $_ -notmatch '\*' -and (Test-Path $_ -PathType Container) } | Select-Object -First 1
if (-not $configured) { throw 'No existing, non-wildcard MonitoredPaths entry is available for the smoke test.' }
$root = $configured
$file = Join-Path $root "winfimlog-phase1-$([Guid]::NewGuid()).txt"
New-Item -ItemType File -Force $file | Out-Null
Start-Sleep -Milliseconds 750
Set-Content $file ([Guid]::NewGuid().ToString())
Start-Sleep -Milliseconds 750
Remove-Item $file -Force
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name = 'WinFIMLogPhase1Smoke'
New-ItemProperty -Path $run -Name $name -Value 'cmd.exe /c exit' -PropertyType String -Force | Out-Null
Start-Sleep -Seconds 3
Remove-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue
$events = Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName=$source; StartTime=$start } |
  Where-Object Id -in 7776,7777,7778,7787
$events | Select-Object TimeCreated, Id, Message | Format-Table -Wrap
$found = @($events.Id | Sort-Object -Unique)
$missing = @(7776,7777,7778,7787 | Where-Object { $_ -notin $found })
if ($missing.Count) { throw "Phase 1 smoke test missed event IDs: $($missing -join ', '). Verify the smoke directory and HKCU Run key are monitored." }
Write-Host 'Phase 1 smoke test passed.' -ForegroundColor Green
