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
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'A supported R&C1 ISO is required.' }
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$windowArgs = @(Get-ObpWindowArguments)
& $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' $RenderingMethod '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL0' '--fixed-seed' $Seed '--rac1-enemy-smoke'
exit $LASTEXITCODE
