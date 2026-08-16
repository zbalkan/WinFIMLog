#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
'WinFIMLog-Diagnostic','WinFIMLog-Baseline','WinFIMLog-Operational' | ForEach-Object {
    if ([Diagnostics.EventLog]::Exists($_)) { Remove-EventLog -LogName $_ }
}
