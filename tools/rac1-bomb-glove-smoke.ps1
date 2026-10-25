#Requires -Version 5.1
# Staged host integration. Rendered mode uses an unfocusable offscreen window.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [string]$CaptureDirectory,
    [string]$State,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
[string[]]$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540') } else { @('--headless') }
$previousCaptureDirectory = $env:OBP_BOMB_CAPTURE_DIR
$previousReproState = $env:OBP_BOMB_REPRO_STATE
try {
    if ($CaptureDirectory) { $env:OBP_BOMB_CAPTURE_DIR = $CaptureDirectory }
    $env:OBP_BOMB_REPRO_STATE = if ($State) { [IO.Path]::GetFullPath($State) } else { $null }
    & $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL0' '--fixed-seed' '121' '--rac1-bomb-glove-smoke'
    $code = $LASTEXITCODE
}
finally {
    $env:OBP_BOMB_CAPTURE_DIR = $previousCaptureDirectory
    $env:OBP_BOMB_REPRO_STATE = $previousReproState
}
exit $code
