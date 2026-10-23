#Requires -Version 5.1
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$path = Join-Path $desktop 'OBP Bug Capture.lnk'
if ($Uninstall) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    return
}
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($path)
$link.TargetPath = (Get-Command powershell.exe).Source
$launcher = Join-Path $root 'tools/launch-bug-capture.ps1'
$link.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"'
$link.WorkingDirectory = $root
$link.IconLocation = (Join-Path $root 'game/assets/branding/obp-icon.ico') + ',0'
$link.Description = 'Record ordinary R&C1 play and save replay-backed bug reports. F8 or paused Square.'
$link.WindowStyle = 7
$link.Save()
Write-Host 'Installed OBP Bug Capture desktop shortcut.'
