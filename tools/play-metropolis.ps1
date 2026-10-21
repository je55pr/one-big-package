#Requires -Version 5.1
<#
.SYNOPSIS
  Play R&C1 Metropolis directly in the isolated OBP worktree.
.DESCRIPTION
  Builds the local Godot C# host (Debug) and launches the ordinary
  interactive Kerwan world using the configured R&C1 disc. No PCSX2.
.EXAMPLE
  .\tools\play-metropolis.ps1
  .\tools\play-metropolis.ps1 -SkipBuild
#>
param(
    [string]$Rac1Iso = $(if ($env:OBP_RAC1_ISO) {
        $env:OBP_RAC1_ISO
    } else {
        'C:\Emu\PS2\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso'
    }),
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
if (-not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
    throw "R&C1 ISO not found: $Rac1Iso. Set OBP_RAC1_ISO or supply -Rac1Iso."
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$argsForGame = @(Get-ObpWindowArguments) + @(
    '--path', (Join-Path $root 'game'),
    '--',
    '--rac1-iso', $Rac1Iso,
    '--destination', 'rac1:LEVEL3'
)
& $godot @argsForGame
exit $LASTEXITCODE
