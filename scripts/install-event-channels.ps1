#Requires -RunAsAdministrator
[CmdletBinding()]
param([int64]$MaxBytes = 134217728)
$ErrorActionPreference = 'Stop'

# The WinFIMLog channel access descriptor grants write access to the service SID.
& sc.exe sidtype WinFIMLog unrestricted | Out-Null
if ($LASTEXITCODE) { throw "Could not enable the WinFIMLog service SID: $LASTEXITCODE" }
$serviceSid = (sc.exe showsid WinFIMLog | Select-String 'S-1-5-80-[0-9-]+').Matches.Value
if (-not $serviceSid) { throw 'Install the WinFIMLog service before installing its Event Log.' }

$source = 'WinFIMLog'
$logName = 'WinFIMLog'
$sddl = "O:BAG:SYD:(A;;0x2;;;$serviceSid)(A;;0xf0007;;;SY)(A;;0x1;;;BA)(A;;0x1;;;S-1-5-32-573)"
if (-not [Diagnostics.EventLog]::SourceExists($source)) {
    New-EventLog -LogName $logName -Source $source
}
else {
    $registeredLog = [Diagnostics.EventLog]::LogNameFromSourceName($source, '.')
    if (-not $registeredLog -or -not $registeredLog.Equals($logName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Event Log source '$source' is registered to '$registeredLog', not '$logName'."
    }
}

& wevtutil.exe sl $logName "/ms:$MaxBytes" /rt:false "/ca:$sddl"
if ($LASTEXITCODE) { throw "wevtutil failed for $logName: $LASTEXITCODE" }
