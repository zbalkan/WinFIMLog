#Requires -RunAsAdministrator
[CmdletBinding()]
param([int64]$OperationalBytes = 134217728, [int64]$BaselineBytes = 67108864)
$ErrorActionPreference = 'Stop'
$serviceSid = (sc.exe showsid WinFIMLog | Select-String 'S-1-5-80-[0-9-]+').Matches.Value
if (-not $serviceSid) { throw 'Install the WinFIMLog service before installing its Event Log channels.' }
$sddl = "O:BAG:SYD:(A;;0x2;;;$serviceSid)(A;;0xf0007;;;SY)(A;;0x1;;;BA)(A;;0x1;;;S-1-5-32-573)"
# Classic Event Log names must differ within their first eight characters.
$channels = @(
    @{ Log='WinFIM-Operational'; Source='WinFIM-Operational'; Bytes=$OperationalBytes },
    @{ Log='WinFIM-Baseline'; Source='WinFIM-Baseline'; Bytes=$BaselineBytes },
    @{ Log='WinFIM-Diagnostic'; Source='WinFIM-Diagnostic'; Bytes=33554432 }
)
foreach ($channel in $channels) {
    if (-not [Diagnostics.EventLog]::SourceExists($channel.Source)) {
        New-EventLog -LogName $channel.Log -Source $channel.Source
    }
    & wevtutil.exe sl $channel.Log "/ms:$($channel.Bytes)" /rt:false "/ca:$sddl"
    if ($LASTEXITCODE) { throw "wevtutil failed for $($channel.Log): $LASTEXITCODE" }
}
