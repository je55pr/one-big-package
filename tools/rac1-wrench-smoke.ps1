#Requires -Version 5.1
# Fixed-input attack/animation/throw gate with explicitly staged contact fixtures.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$RenderingMethod = 'gl_compatibility',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
$windowArgs = @(Get-ObpWindowArguments)
$code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList (@($windowArgs) + @('--path', (Join-Path $root 'game'), '--rendering-method', $RenderingMethod, '--', '--rac1-iso', $Rac1Iso, '--destination', 'rac1:LEVEL0', '--rac1-wrench-smoke')) `
    -CompletionMarker '[lifecycle] quit-request reason=rac1-wrench-smoke-pass exitCode=0' -WorkingDirectory $root
exit $code
