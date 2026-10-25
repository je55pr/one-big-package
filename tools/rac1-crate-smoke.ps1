#Requires -Version 5.1
# Staged Godot fixtures exercise source-owned crate/pickup behavior on two retail levels.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$RenderingMethod = 'gl_compatibility',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
foreach ($level in @(0, 1)) {
    $windowArgs = @(Get-ObpWindowArguments)
    & $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' $RenderingMethod '--' '--rac1-iso' $Rac1Iso '--destination' "rac1:LEVEL$level" '--rac1-crate-smoke'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
