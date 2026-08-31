#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$source = 'WinFIMLog'
$start = Get-Date
$policy = 'HKLM:\SOFTWARE\Policies\WinFIMLog'
$preference = 'HKLM:\SOFTWARE\WinFIMLog'
$config = if ((Get-ItemProperty $policy -Name MonitoredPaths -ErrorAction SilentlyContinue).MonitoredPaths) { $policy } else { $preference }
$configured = (Get-ItemProperty $config -Name MonitoredPaths).MonitoredPaths |`
  ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) } |`
  Where-Object { $_ -notmatch '\*' -and (Test-Path $_ -PathType Container) } | Select-Object -First 1
if (-not $configured) { throw 'No existing, non-wildcard MonitoredPaths entry is available for the smoke test.' }
$root = $configured
$file = Join-Path $root "winfimlog-phase1-$([Guid]::NewGuid()).txt"
$copy = Join-Path $root "winfimlog-phase1-copy-$([Guid]::NewGuid()).txt"
$renamed = Join-Path $root "winfimlog-phase1-renamed-$([Guid]::NewGuid()).txt"
New-Item -ItemType File -Force $file | Out-Null
Start-Sleep -Milliseconds 750
Set-Content $file ([Guid]::NewGuid().ToString())
Start-Sleep -Milliseconds 750
Copy-Item $file $copy
Start-Sleep -Milliseconds 750
Move-Item $file $renamed
Start-Sleep -Milliseconds 750
& icacls.exe $renamed /inheritance:d | Out-Null
if ($LASTEXITCODE) { throw "Could not change the smoke file ACL: $LASTEXITCODE" }
Start-Sleep -Milliseconds 750
Remove-Item $renamed,$copy -Force
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name = 'WinFIMLogPhase1Smoke'
# reg.exe exits before attribution enrichment, exercising the unavailable
# short-lived-process path without suppressing the registry observation.
$nativeRun = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run'
$writer = Start-Process reg.exe -ArgumentList @('add', $nativeRun, '/v', $name, '/t', 'REG_SZ', '/d', 'cmd.exe /c exit', '/f') -Wait -PassThru -WindowStyle Hidden
if ($writer.ExitCode -ne 0) { throw "Short-lived registry writer exited with $($writer.ExitCode)." }
Start-Sleep -Milliseconds 750
$child = "$nativeRun\WinFIMLogPhase1-$([Guid]::NewGuid().ToString('N'))"
& reg.exe add $child /f | Out-Null
if ($LASTEXITCODE) { throw "Registry key creation exited with $LASTEXITCODE." }
& reg.exe add $child /v Enabled /t REG_DWORD /d 1 /f | Out-Null
if ($LASTEXITCODE) { throw "Registry value modification exited with $LASTEXITCODE." }
Start-Sleep -Milliseconds 750
& reg.exe delete $child /f | Out-Null
if ($LASTEXITCODE) { throw "Registry key deletion exited with $LASTEXITCODE." }
Start-Sleep -Seconds 3
Remove-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue
$events = Get-WinEvent -FilterHashtable @{ LogName='WinFIMLog'; ProviderName=$source; StartTime=$start } |
  Where-Object Id -in 7776,7777,7778,7786,7787,7788
$events | Select-Object TimeCreated, Id, Message | Format-Table -Wrap
$found = @($events.Id | Sort-Object -Unique)
$missing = @(7776,7777,7778,7786,7787,7788 | Where-Object { $_ -notin $found })
if ($missing.Count) { throw "Phase 1 smoke test missed event IDs: $($missing -join ', '). Verify the smoke directory and HKCU Run key are monitored." }
$expectedTypes = @{ 7776='FileSystemFinding'; 7777='FileSystemFinding'; 7778='FileSystemFinding'; 7786='RegistryFinding'; 7787='RegistryFinding'; 7788='RegistryFinding' }
$records = @()
foreach ($log in $events) {
  try { $record = $log.Message | ConvertFrom-Json }
  catch { throw "Event $($log.Id) was written, but its message is not a structured EventContract: $($_.Exception.Message)" }
  if ($record.eventId -ne $log.Id) {
    throw "Event Log ID $($log.Id) does not match envelope eventId $($record.eventId)."
  }
  if ($record.recordType -ne $expectedTypes[$log.Id]) {
    throw "Event $($log.Id) used record type '$($record.recordType)' instead of '$($expectedTypes[$log.Id])'."
  }
  $records += $record
}
$renameRecord = $records | Where-Object { $_.eventId -eq 7777 -and $_.fields.operation -eq 'RenamedOrMoved' -and $_.fields.oldPath -eq $file -and $_.fields.newPath -eq $renamed -and $_.fields.renameCorrelationMethod -eq 'RuntimeAdjacentBufferPair' -and $_.fields.renameCorrelationConfidence -eq 'Low' } | Select-Object -First 1
if (-not $renameRecord) { throw 'The filesystem rename/move did not retain its old and new paths in event 7777.' }
$copyRecord = $records | Where-Object { $_.eventId -eq 7776 -and $_.fields.path -eq $copy -and $_.fields.operation -eq 'Created' } | Select-Object -First 1
if (-not $copyRecord) { throw 'The copied destination was not written as filesystem creation event 7776.' }
$aclRecord = $records | Where-Object { $_.eventId -eq 7777 -and $_.fields.path -eq $renamed -and $_.fields.currentAcl -and $_.fields.currentAcl -ne $_.fields.previousAcl } | Select-Object -First 1
if (-not $aclRecord) { throw 'The ACL-only modification was not written with current and previous ACL evidence.' }
$deleteRecord = $records | Where-Object { $_.eventId -eq 7778 -and $_.fields.path -eq $copy -and $_.fields.objectType -eq 'File' -and $null -ne $_.fields.previousSizeBytes } | Select-Object -First 1
if (-not $deleteRecord) { throw 'The deletion did not recover file type and size from the previous projection.' }
Write-Host 'Phase 1 smoke test passed.' -ForegroundColor Green
