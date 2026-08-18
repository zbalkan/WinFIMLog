#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
# Remove the consolidated log and obsolete split logs from earlier installations.
'WinFIMLog','WinFIM-Diagnostic','WinFIM-Baseline','WinFIM-Operational',
'WinFIMLog-Diagnostic','WinFIMLog-Baseline','WinFIMLog-Operational' | ForEach-Object {
    if ([Diagnostics.EventLog]::Exists($_)) { Remove-EventLog -LogName $_ }
}
