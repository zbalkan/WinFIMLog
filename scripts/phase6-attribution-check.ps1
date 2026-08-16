#Requires -RunAsAdministrator
[CmdletBinding()]
param([switch]$SaclTier)

$ErrorActionPreference = 'Stop'

Write-Host 'Checking kernel process provider and WinFIMLog service state...'
$service = Get-Service WinFIMLog
if ($service.Status -ne 'Running') { throw 'WinFIMLog is not running.' }
logman query providers 'Microsoft-Windows-Kernel-Process' | Out-Host

if ($SaclTier) {
    Write-Host 'Checking the declared SACL-tier dependencies...'
    $policy = auditpol /get /subcategory:'File System','Registry'
    $policy | Out-Host
    if ($LASTEXITCODE -ne 0 -or ($policy -join "`n") -notmatch 'Success|Failure') {
        throw 'Required File System/Registry object-access auditing is not enabled.'
    }
    Get-WinEvent -LogName Security -MaxEvents 1 -ErrorAction Stop | Out-Null
}

Write-Host 'PASS: attribution prerequisites are observable. Run the unit suite for PID-reuse isolation.'
