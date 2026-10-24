#Requires -Version 5.1
# Validate or generate C# UID sidecars using only .NET 8 and pinned Godot .NET.
param(
  [switch]$Check,
  [string]$Godot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'OBP.GodotUidTool/OBP.GodotUidTool.csproj'
$arguments = @('run', '--project', $project, '--', $(if ($Check) { '--check' } else { '--sync' }), '--root', $root)
if ($Godot) { $arguments += @('--godot', $Godot) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
