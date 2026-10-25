#Requires -Version 5.1
# Sends logical joypad events to real Godot UI, without desktop input or user-setting writes.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [string]$CaptureDirectory,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OBP.Game.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$sourceArgs = @()
if ($Rac1Iso) { $sourceArgs += @('--rac1-iso', $Rac1Iso) }
if ($GcIso) { $sourceArgs += @('--gc-iso', $GcIso) }
if ($UyaIso) { $sourceArgs += @('--uya-iso', $UyaIso) }
[string[]]$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') } else { @('--headless') }
$previousCapture = $env:OBP_CONTROLLER_CAPTURE_DIR
try {
    if ($CaptureDirectory) { $env:OBP_CONTROLLER_CAPTURE_DIR = [IO.Path]::GetFullPath($CaptureDirectory) }
    & $godot @windowArgs '--fixed-fps' '60' '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' 'res://scenes/ControllerSmoke.tscn' '--' '--controller-smoke' @sourceArgs
    $result = $LASTEXITCODE
}
finally { $env:OBP_CONTROLLER_CAPTURE_DIR = $previousCapture }
exit $result
