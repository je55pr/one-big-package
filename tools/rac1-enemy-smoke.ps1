#Requires -Version 5.1
# Staged host integration; not a live retail replay or an ordinary-play route.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$RenderingMethod = 'gl_compatibility',
    [int]$Seed = 1440,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
$windowArgs = @(Get-ObpWindowArguments)
$code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList (@($windowArgs) + @('--path', (Join-Path $root 'game'), '--rendering-method', $RenderingMethod, '--', '--rac1-iso', $Rac1Iso, '--destination', 'rac1:LEVEL0', '--fixed-seed', $Seed, '--rac1-enemy-smoke')) `
    -CompletionMarker '[lifecycle] quit-request reason=rac1-enemy-smoke-pass exitCode=0' -WorkingDirectory $root
exit $code
