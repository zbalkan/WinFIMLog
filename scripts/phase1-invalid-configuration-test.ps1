#Requires -RunAsAdministrator
param([string]$ServiceName = 'WinFIMLog')
$ErrorActionPreference = 'Stop'
$policy = 'HKLM:\SOFTWARE\Policies\WinFIMLog'
$preference = 'HKLM:\SOFTWARE\WinFIMLog'
$key = if ((Get-ItemProperty $policy -Name MonitoredPaths -ErrorAction SilentlyContinue).MonitoredPaths) { $policy } else { $preference }
$original = (Get-ItemProperty $key -Name MonitoredPaths).MonitoredPaths
$invalid = 'relative\invalid-phase1-path'
try {
    Set-ItemProperty $key -Name MonitoredPaths -Value @($invalid)
    Stop-Service $ServiceName -ErrorAction SilentlyContinue
    $start = Get-Date
    try { Start-Service $ServiceName -ErrorAction Stop } catch { }
    Start-Sleep -Seconds 5
    if ((Get-Service $ServiceName).Status -eq 'Running') { throw 'Service started with invalid configuration.' }
    $diagnostic = Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='WinFIMLog'; StartTime=$start } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -like "*$invalid*" } | Select-Object -First 1
    if (-not $diagnostic) { throw "Startup rejection did not name invalid value '$invalid'." }
    $diagnostic | Select-Object TimeCreated, Id, Message | Format-List
}
finally {
    Set-ItemProperty $key -Name MonitoredPaths -Value $original
    Start-Service $ServiceName
}
Write-Host 'Invalid startup configuration test passed.' -ForegroundColor Green
