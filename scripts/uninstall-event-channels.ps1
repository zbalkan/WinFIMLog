#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
# Include the former names so uninstall also cleans up a partially completed legacy installation.
'WinFIM-Diagnostic','WinFIM-Baseline','WinFIM-Operational',
'WinFIMLog-Diagnostic','WinFIMLog-Baseline','WinFIMLog-Operational' | ForEach-Object {
    if ([Diagnostics.EventLog]::Exists($_)) { Remove-EventLog -LogName $_ }
}
