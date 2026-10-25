#Requires -Version 5.1
# Retail-backed, staged Godot host route gate (not ordinary navigation).
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
    throw 'An authorized R&C1 ISO is required.'
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game -Quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$windowArgs = @(Get-ObpWindowArguments)
& $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL3' '--rac1-metropolis-taxi-smoke'
exit $LASTEXITCODE
