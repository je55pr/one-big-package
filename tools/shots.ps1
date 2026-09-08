#Requires -Version 5.1
<#
.SYNOPSIS
  Run a named shot list against a world - one process, every shot in the list.
  Writes captures/shots/<world>-<shot>.png + a .json sidecar of the
  deterministic metadata (geometry counts, camera pose, overlay state).

.DESCRIPTION
  Wraps the game's --shots runner. -Game picks tools/shots/<game>.json and the
  right retail ISO. -Check also diffs each sidecar against the committed golden
  in tools/shots/golden/ (via tools/shots/check.py); a missing golden is written
  for review, a changed one fails.

.EXAMPLE
  ./tools/shots.ps1 -Game rac2
  ./tools/shots.ps1 -Game rac1 -Check
  ./tools/shots.ps1 -List tools/shots/rac2.json -Worlds oozla,siberius
#>
param(
  [ValidateSet('rac1', 'rac2', 'rac3')][string]$Game = 'rac2',
  [string]$List = '',
  [string[]]$Worlds = @(),
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$GcIso = $env:OBP_GC_ISO,
  [string]$Rac3Iso = $env:OBP_UYA_ISO,
  [string]$RenderingMethod = 'gl_compatibility',
  [string]$Resolution = '1280x720',
  [switch]$Check,
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $List) { $List = "tools/shots/$Game.json" }
$listPath = Join-Path $root $List
if (-not (Test-Path $listPath)) { Write-Error "shot list not found: $listPath"; exit 2 }

$inbox = 'C:\Claude\Inbox'
if (-not $Rac1Iso) { $Rac1Iso = Join-Path $inbox 'Ratchet & Clank (USA) (En,Fr,De,Es,It).iso' }
if (-not $GcIso)   { $GcIso   = Join-Path $inbox 'Ratchet & Clank - Going Commando (USA) (v1.01).iso' }
if (-not $Rac3Iso) { $Rac3Iso = Join-Path $inbox 'Ratchet & Clank - Up Your Arsenal (USA) (En,Fr,Es).iso' }

$isoArgs = @()
if (Test-Path $Rac1Iso) { $isoArgs += @('--rac1-iso', $Rac1Iso) }
if (Test-Path $GcIso)   { $isoArgs += @('--gc-iso', $GcIso) }
if (Test-Path $Rac3Iso) { $isoArgs += @('--uya-iso', $Rac3Iso) }
if (-not $isoArgs) { Write-Error "no retail ISOs found (set OBP_RAC1_ISO / OBP_GC_ISO / OBP_UYA_ISO)"; exit 2 }

$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }

if (-not $SkipBuild) {
  & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not (Test-Path (Join-Path $root 'game/.godot'))) {
  & $godot --headless --path (Join-Path $root 'game') --import
}

$shotsDir = Join-Path $root 'captures/shots'
New-Item -ItemType Directory -Force -Path $shotsDir | Out-Null

# Worlds default: the list's own pinned world (pass nothing) unless overridden.
$worldSpecs = if ($Worlds.Count -gt 0) { $Worlds } else { , @('') }
$gamePath = Join-Path $root 'game'

$fail = 0
foreach ($w in $worldSpecs) {
  $label = if ($w) { $w } else { '(list world)' }
  Write-Host "`n=== $Game  $label ===" -ForegroundColor Cyan
  $godotArgs = @('--path', $gamePath, '--rendering-method', $RenderingMethod, '--resolution', $Resolution, '--')
  $godotArgs += $isoArgs
  $godotArgs += @('--shots', $listPath, '--shots-out', $shotsDir)
  if ($w) { $godotArgs += @('--shots-world', $w) }
  & $godot @godotArgs
  if ($LASTEXITCODE -ne 0) { Write-Warning "shots run failed ($label, exit $LASTEXITCODE)"; $fail++ }
}

if ($Check) {
  Write-Host "`n--- golden check ---" -ForegroundColor Cyan
  & python (Join-Path $PSScriptRoot 'shots/check.py') $shotsDir (Join-Path $PSScriptRoot 'shots/golden')
  if ($LASTEXITCODE -ne 0) { $fail++ }
}

if ($fail) { Write-Error "$fail failure(s)"; exit 1 }
Write-Host "`nshots written to captures/shots/" -ForegroundColor Green
