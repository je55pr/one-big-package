#Requires -Version 5.1
# Generate missing .cs.uid sidecars via the pinned Godot .NET editor.
# Use -Check for a fast read-only CI/contributor validation.
param(
  [switch]$Check,
  [string]$Godot
)
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'sync-godot-uids.mjs'
$arguments = @($scriptPath, $(if ($Check) { '--check' } else { '--sync' }))
if ($Godot) { $arguments += @('--godot', $Godot) }
& node @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
