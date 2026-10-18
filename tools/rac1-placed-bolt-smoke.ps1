#Requires -Version 5.1
# Authored currency bolts: Veldin, Batalia's floating placements, Pokitaru's fives.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$RenderingMethod = 'gl_compatibility',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
    throw 'A supported R&C1 ISO is required.'
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
foreach ($level in @(0, 8, 11)) {
    & $godot '--path' (Join-Path $root 'game') '--rendering-method' $RenderingMethod '--resolution' '1280x720' '--' '--rac1-iso' $Rac1Iso '--destination' "rac1:LEVEL$level" '--rac1-placed-bolt-smoke'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
