#Requires -Version 5.1
<#
.SYNOPSIS
  Run a named shot list against one or more GC worlds - one process per world,
  every shot in the list per world. Writes captures/shots/<world>-<shot>.png and
  a .json sidecar of the deterministic metadata (geometry counts, camera pose,
  overlay state).

.DESCRIPTION
  Wraps the game's --shots runner. Pass -Check to also diff each sidecar against
  the committed golden in tools/shots/golden/ (via tools/shots/check.py); a
  missing golden is written for review, a changed one fails.

.EXAMPLE
  ./tools/shots.ps1
  ./tools/shots.ps1 -Worlds oozla,siberius -List tools/shots/showcase.json
  ./tools/shots.ps1 -Check
#>
param(
  [string]$GcIso = $env:OBP_GC_ISO,
  [string[]]$Worlds = @('oozla', 'siberius'),
  [string]$List = 'tools/shots/showcase.json',
  [string]$RenderingMethod = 'gl_compatibility',
  [string]$Resolution = '1280x720',
  [switch]$Check,
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $GcIso -or -not (Test-Path $GcIso)) {
  $GcIso = 'C:\Claude\Inbox\Ratchet & Clank - Going Commando (USA) (v1.01).iso'
}
if (-not (Test-Path $GcIso)) { Write-Error "GC ISO not found: $GcIso"; exit 2 }

$listPath = Join-Path $root $List
if (-not (Test-Path $listPath)) { Write-Error "shot list not found: $listPath"; exit 2 }

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

$fail = 0
foreach ($w in $Worlds) {
  Write-Host "`n=== $w ===" -ForegroundColor Cyan
  & $godot --path (Join-Path $root 'game') --rendering-method $RenderingMethod --resolution $Resolution `
    -- --gc-iso $GcIso --shots $listPath --shots-world $w --shots-out $shotsDir
  if ($LASTEXITCODE -ne 0) { Write-Warning "shots run failed for $w (exit $LASTEXITCODE)"; $fail++ }
}

if ($Check) {
  Write-Host "`n--- golden check ---" -ForegroundColor Cyan
  & python (Join-Path $PSScriptRoot 'shots/check.py') $shotsDir (Join-Path $PSScriptRoot 'shots/golden')
  if ($LASTEXITCODE -ne 0) { $fail++ }
}

if ($fail) { Write-Error "$fail failure(s)"; exit 1 }
Write-Host "`nshots written to captures/shots/" -ForegroundColor Green
