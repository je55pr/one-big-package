#Requires -Version 5.1
<#
.SYNOPSIS
  Put a "One Big Package" shortcut on the Windows desktop that launches the
  production showcase build (tools/obp.cmd) with the OBP icon.

.DESCRIPTION
  Idempotent - re-run it any time to refresh the shortcut (for example after
  moving the repo). Removes it with -Uninstall. Nothing outside the desktop
  shortcut file is modified.

.EXAMPLE
  ./tools/install-desktop-shortcut.ps1
  ./tools/install-desktop-shortcut.ps1 -Uninstall
#>
param(
  [string]$Name = 'One Big Package',
  [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$linkPath = Join-Path $desktop "$Name.lnk"

if ($Uninstall) {
  if (Test-Path $linkPath) { Remove-Item $linkPath; Write-Host "Removed $linkPath" }
  else { Write-Host "No shortcut at $linkPath" }
  return
}

$target = Join-Path $root 'tools/obp.cmd'
$icon = Join-Path $root 'game/assets/branding/obp-icon.ico'
if (-not (Test-Path $target)) { Write-Error "Launcher not found: $target"; exit 2 }
if (-not (Test-Path $icon)) { Write-Error "Icon not found: $icon"; exit 2 }

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = $target
$link.WorkingDirectory = $root
$link.IconLocation = "$icon,0"
$link.Description = 'One Big Package - PS2 Ratchet & Clank trilogy, reconstructed runtime (showcase build)'
$link.WindowStyle = 7   # start minimised; the build console is brief, the game opens maximised
$link.Save()

Write-Host "Shortcut created: $linkPath" -ForegroundColor Green
Write-Host "  target : $target"
Write-Host "  icon   : $icon"
