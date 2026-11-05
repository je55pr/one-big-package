#Requires -Version 5.1
# Staged Godot fixtures exercise source-owned crate/pickup behavior on two retail levels.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$RenderingMethod = 'gl_compatibility',
    [switch]$SkipBuild,
    [switch]$Render
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
foreach ($level in @(0, 1)) {
    $windowArgs = if ($Render) { @(Get-ObpWindowArguments) } else { @('--headless') }
    $code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList (@($windowArgs) + @('--path', (Join-Path $root 'game'), '--rendering-method', $RenderingMethod, '--', '--rac1-iso', $Rac1Iso, '--destination', "rac1:LEVEL$level", '--no-profile', '--rac1-crate-smoke')) `
        -CompletionMarker '[lifecycle] quit-request reason=rac1-crate-smoke-pass exitCode=0' -WorkingDirectory $root
    if ($code -ne 0) { exit $code }
}
exit 0
