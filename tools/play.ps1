#Requires -Version 5.1
<#
.SYNOPSIS
  Launch OBP in interactive player mode for quick game-feel testing: load a
  Going Commando ISO, import a level, spawn the debug capsule. WASD move,
  mouse look, Space jump, F fly / noclip (Space/E up, Ctrl/Q down, Shift
  boost), R respawn, Tab free cursor, Esc quit. The on-screen HUD reports
  position, speed and the last jump's air time / distance / apex.

.DESCRIPTION
  Builds game/ (Debug) so the latest code is used, then runs the pinned Godot
  build against game/ with `--test-scene player`. With no ISO available it falls
  back to the on-screen disc picker.

.EXAMPLE
  ./tools/play.ps1
  ./tools/play.ps1 -GcIso "D:\isos\Going Commando.iso" -GcLevel 1
  ./tools/play.ps1 -SkipBuild
#>
param(
  [string]$GcIso = $env:OBP_GC_ISO,
  [int]$GcLevel = 1,
  [string]$Planet = '',
  [string]$RenderingMethod = 'gl_compatibility',
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# --- resolve the disc image ---------------------------------------------------
if (-not $GcIso -or -not (Test-Path $GcIso)) {
  $candidates = @(
    $GcIso,
    'C:\Claude\Inbox\Ratchet & Clank - Going Commando (USA) (v1.01).iso'
  ) | Where-Object { $_ -and (Test-Path $_) }
  $GcIso = $candidates | Select-Object -First 1
}

if ($GcIso) {
  if ($Planet) {
    Write-Host "OBP: straight to $Planet from $GcIso" -ForegroundColor Cyan
    $sceneArgs = @('--gc-iso', $GcIso, '--planet', $Planet)
  } elseif ($PSBoundParameters.ContainsKey('GcLevel')) {
    Write-Host "OBP: straight to LEVEL$GcLevel from $GcIso" -ForegroundColor Cyan
    $sceneArgs = @('--gc-iso', $GcIso, '--planet', "$GcLevel")
  } else {
    Write-Host "OBP: planet selector from $GcIso" -ForegroundColor Cyan
    $sceneArgs = @('--gc-iso', $GcIso)
  }
} else {
  Write-Warning "No Going Commando ISO found (set OBP_GC_ISO or pass -GcIso). Launching the disc picker."
  $sceneArgs = @('--test-scene', 'picker')
}

# --- pinned Godot ------------------------------------------------------------
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $godot)) {
  & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}

# --- build + import --------------------------------------------------------
if (-not $SkipBuild) {
  & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
  if ($LASTEXITCODE -ne 0) {
    Read-Host "`nBuild failed - press Enter to close"
    exit $LASTEXITCODE
  }
}
if (-not (Test-Path (Join-Path $root 'game/.godot'))) {
  & $godot --headless --path (Join-Path $root 'game') --import
}

# --- run --------------------------------------------------------------------
Write-Host "Controls: WASD - mouse look - Space jump - F fly - R respawn - Tab cursor - Esc quit`n" -ForegroundColor DarkGray
& $godot --path (Join-Path $root 'game') --rendering-method $RenderingMethod -- @sceneArgs
$code = $LASTEXITCODE
if ($code -ne 0) { Read-Host "`nGodot exited with $code - press Enter to close" }
exit $code
