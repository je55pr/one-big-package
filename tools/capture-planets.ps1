#Requires -Version 5.1
<#
.SYNOPSIS
  Deterministic Going Commando showcase capture set. For each planet: build ->
  launch Godot -> import the level from the retail ISO -> park a fixed camera ->
  screenshot + metadata JSON. One process per planet (proves the load path from
  a cold start); the in-engine switching stress test is separate.

.EXAMPLE
  ./tools/capture-planets.ps1
  ./tools/capture-planets.ps1 -Planets oozla,endako,grelbin -Player
#>
param(
  [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
  [string[]]$Planets = @('oozla', 'endako', 'tabora', 'siberius', 'damosel'),
  [int]$Frame = 210,
  [string]$RenderingMethod = 'gl_compatibility',
  [string]$Resolution = '1600x900',
  [switch]$Framed,
  [switch]$SkipBuild
)
# Default: scripted debug player (grounded on real collision near the ship —
# reliable "standing on this planet" shot). -Framed uses the fixed overview cam.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Rac2Iso -or -not (Test-Path $Rac2Iso)) {
  $Rac2Iso = 'C:\Claude\Inbox\Ratchet & Clank - Going Commando (USA) (v1.01).iso'
}
if (-not (Test-Path $Rac2Iso)) { Write-Error "GC ISO not found: $Rac2Iso"; exit 2 }

$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }

if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot 'build.ps1') -Game
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not (Test-Path (Join-Path $root 'game/.godot'))) {
  & $godot --headless --path (Join-Path $root 'game') --import
}

$capDir = Join-Path $root 'captures'
New-Item -ItemType Directory -Force -Path $capDir | Out-Null

$fail = 0
foreach ($p in $Planets) {
  $out = Join-Path $capDir "gc-showcase-$p.png"
  Write-Host "`n=== $p ===" -ForegroundColor Cyan
  $sceneArgs = @('--rac2-iso', $Rac2Iso, '--planet', $p, '--direct',
                 '--capture-frame', "$Frame", '--capture-out', $out)
  if (-not $Framed) { $sceneArgs = @('--test-scene', 'player') + $sceneArgs }

  & $godot --path (Join-Path $root 'game') --rendering-method $RenderingMethod --resolution $Resolution -- @sceneArgs
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) {
    Write-Warning "capture failed for $p (exit $LASTEXITCODE)"
    $fail++
  } else {
    Write-Host "capture: $out" -ForegroundColor Green
  }
}

if ($fail) { Write-Error "$fail planet capture(s) failed"; exit 1 }
Write-Host "`nAll $($Planets.Count) planet captures written to captures/." -ForegroundColor Green
