#Requires -Version 5.1
# Build the OBP native solution (all C# libraries + the Godot game assembly).
param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $root 'OneBigPackage.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
