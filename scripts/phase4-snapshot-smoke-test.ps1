#Requires -RunAsAdministrator
param(
    [string]$ServiceName = 'WinFIMLog',
    [int]$TimeoutMinutes = 60
)
$ErrorActionPreference = 'Stop'
$preferences = 'HKLM:\SOFTWARE\WinFIMLog'
$policy = 'HKLM:\SOFTWARE\Policies\WinFIMLog'
$scopeConfiguration = if ((Get-ItemProperty $policy -Name MonitoredPaths -ErrorAction SilentlyContinue).MonitoredPaths) { $policy } else { $preferences }
$root = (Get-ItemProperty $scopeConfiguration -Name MonitoredPaths).MonitoredPaths |
    ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) } |
    Where-Object { $_ -notmatch '\*' -and (Test-Path $_ -PathType Container) } |
    Select-Object -First 1
if (-not $root) { throw 'No concrete monitored root is available for the Phase 4 check.' }

function Wait-WinFIMLogEvent([datetime]$after, [scriptblock]$predicate, [string]$description,
    [string]$logName = 'Application', [string]$providerName = 'WinFIMLog') {
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        $event = Get-WinEvent -FilterHashtable @{ LogName=$logName; ProviderName=$providerName; StartTime=$after } -ErrorAction SilentlyContinue |
            Where-Object $predicate | Select-Object -First 1
        if ($event) { return $event }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $description."
}

# Establish a complete comparison baseline.
$start = Get-Date
Restart-Service $ServiceName
Wait-WinFIMLogEvent $start { $_.Message -match 'Completed filesystem baseline' } 'the initial complete filesystem baseline' | Out-Null

# A persistent change made while stopped must be found by the startup snapshot.
Stop-Service $ServiceName
$offlineFile = Join-Path $root "winfimlog-offline-$([Guid]::NewGuid()).txt"
Set-Content -LiteralPath $offlineFile -Value 'created while WinFIMLog was stopped'
$restart = Get-Date
Start-Service $ServiceName
$finding = Wait-WinFIMLogEvent $restart { $_.Id -eq 7795 -and $_.Message -like "*$offlineFile*" } `
    'event 7795 for the offline persistent change' 'WinFIMLog-Baseline' 'WinFIMLog-Baseline'
$finding | Select-Object TimeCreated, Id, Message | Format-List

# Database deletion must build a complete baseline regardless of the legacy flag.
Stop-Service $ServiceName
$database = Join-Path $env:ProgramData 'FIM\fim.db'
Remove-Item -LiteralPath $database -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "$database-log" -Force -ErrorAction SilentlyContinue
New-ItemProperty -Path $preferences -Name FileDiscoveryCompleted -PropertyType DWord -Value 1 -Force | Out-Null
$databaseRestart = Get-Date
Start-Service $ServiceName
Wait-WinFIMLogEvent $databaseRestart { $_.Message -match 'Completed filesystem baseline' } 'a complete baseline after database deletion' | Out-Null

Remove-Item -LiteralPath $offlineFile -Force -ErrorAction SilentlyContinue
Write-Host 'Phase 4 snapshot smoke test passed.' -ForegroundColor Green
