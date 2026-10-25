#Requires -Version 5.1
# Staged input and playback checks. Rendered runs stay unfocusable and offscreen.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'A supported R&C1 ISO is required.' }
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OBP.Game.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
[string[]]$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540') } else { @('--headless') }
& $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL0' '--fixed-seed' '121' '--rac1-player-audio-smoke'
exit $LASTEXITCODE
