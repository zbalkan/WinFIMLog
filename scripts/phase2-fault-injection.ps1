#Requires -RunAsAdministrator
param([string]$ServiceName = 'WinFIMLog', [int]$BurstCount = 20000)
$ErrorActionPreference = 'Stop'
$preferences = 'HKLM:\SOFTWARE\WinFIMLog'
$policy = 'HKLM:\SOFTWARE\Policies\WinFIMLog'
$scopeConfiguration = if ((Get-ItemProperty $policy -Name MonitoredPaths -ErrorAction SilentlyContinue).MonitoredPaths) { $policy } else { $preferences }
$source = 'WinFIMLog-Operational'
$root = (Get-ItemProperty $scopeConfiguration -Name MonitoredPaths).MonitoredPaths |
    ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) } |
    Where-Object { $_ -notmatch '\*' -and (Test-Path $_ -PathType Container) } |
    Select-Object -First 1
if (-not $root) { throw 'No concrete monitored root is available for fault injection.' }
$scope = Join-Path $root "WinFIMLogBurst-$([Guid]::NewGuid())"
$oldCapacity = (Get-ItemProperty $preferences -Name CaptureQueueCapacity -ErrorAction SilentlyContinue).CaptureQueueCapacity
$oldBuffer = (Get-ItemProperty $preferences -Name WatcherBufferSizeKB -ErrorAction SilentlyContinue).WatcherBufferSizeKB
try {
    New-ItemProperty $preferences -Name CaptureQueueCapacity -PropertyType DWord -Value 1 -Force | Out-Null
    New-ItemProperty $preferences -Name WatcherBufferSizeKB -PropertyType DWord -Value 8 -Force | Out-Null
    Restart-Service $ServiceName
    $start = Get-Date
    New-Item -ItemType Directory -Force $scope | Out-Null
    1..$BurstCount | ForEach-Object { [IO.File]::WriteAllText((Join-Path $scope "$_.txt"), "$_") }

    $deadline = (Get-Date).AddMinutes(5)
    do {
        Start-Sleep -Seconds 2
        $events = @(Get-WinEvent -FilterHashtable @{ LogName='WinFIMLog-Operational'; ProviderName=$source; StartTime=$start } -ErrorAction SilentlyContinue)
        $queueGap = $events | Where-Object { $_.Id -eq 7791 -and $_.Message -match 'QueueFull' } | Select-Object -First 1
    } until ($queueGap -or (Get-Date) -ge $deadline)
    if (-not $queueGap) { throw 'Bounded queue saturation did not produce coverage-gap event 7791.' }

    Restart-Service $ServiceName
    Start-Sleep -Seconds 10
    $heartbeat = Get-WinEvent -FilterHashtable @{ LogName='WinFIMLog-Operational'; ProviderName=$source; StartTime=$start; Id=7790 } -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $heartbeat) { throw 'No health heartbeat was observed after source restart.' }
    $events | Where-Object Id -in 7790,7791,7792,7793 | Select-Object TimeCreated, Id, Message | Format-Table -Wrap
    Write-Host 'Queue saturation and restart checks passed. Watcher/ETW/disk/sink destructive cases remain VM release-gate scenarios.' -ForegroundColor Green
}
finally {
    if ($null -ne $oldCapacity) { New-ItemProperty $preferences -Name CaptureQueueCapacity -PropertyType DWord -Value $oldCapacity -Force | Out-Null }
    if ($null -ne $oldBuffer) { New-ItemProperty $preferences -Name WatcherBufferSizeKB -PropertyType DWord -Value $oldBuffer -Force | Out-Null }
    Remove-Item $scope -Recurse -Force -ErrorAction SilentlyContinue
    Restart-Service $ServiceName -ErrorAction SilentlyContinue
}
