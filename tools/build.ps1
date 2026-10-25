#Requires -Version 5.1
# Build the OBP native solution (all C# libraries + the Godot game assembly).
param(
    [string]$Configuration = 'Debug',
    [switch]$Game,
    [switch]$NoRestore,
    [switch]$WarningsAsErrors,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = if ($Game) { 'game/OBP.Game.csproj' } else { 'OneBigPackage.sln' }
$arguments = @('build', (Join-Path $root $target), '-c', $Configuration, '--nologo')
if ($NoRestore) { $arguments += '--no-restore' }
if ($WarningsAsErrors) { $arguments += '-warnaserror' }
if ($Quiet) { $arguments += @('-v', 'quiet') }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
