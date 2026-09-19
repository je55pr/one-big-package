#Requires -Version 5.1
<#
.SYNOPSIS
  Run the common Ratchet movement host smoke in representative R&C1, GC and UYA worlds.

.DESCRIPTION
  Builds the Godot C# host once, then launches the pinned Godot runtime for
  rac1:LEVEL0, rac2:LEVEL1 and rac3:TABLE1. Each run injects movement through
  the live InputMap boundary and must validate reconstructed collision contact,
  low-stick walk, full-stick run progression, arbitrary-angle steering, crouch,
  variable jump, partial-stick air control, keyboard fallback, development fly
  and development respawn.

  The controller under test is the retail-derived R&C1 controller reused across
  the trilogy as an explicit OBP-created default. Passing GC/UYA does not claim
  that either sequel used the same native movement law.

.EXAMPLE
  ./tools/movement-smoke.ps1 -Rac1Iso D:\isos\RAC1.iso -GcIso D:\isos\GC.iso -UyaIso D:\isos\UYA.iso
#>
param(
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$GcIso = $env:OBP_GC_ISO,
  [string]$UyaIso = $env:OBP_UYA_ISO,
  [string]$RenderingMethod = 'gl_compatibility',
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

foreach ($source in @(
  @{ label='R&C1'; path=$Rac1Iso },
  @{ label='GC'; path=$GcIso },
  @{ label='UYA'; path=$UyaIso }
)) {
  if (-not $source.path -or -not (Test-Path -LiteralPath $source.path -PathType Leaf)) {
    Write-Error "$($source.label) ISO is required for trilogy movement smoke."
    exit 2
  }
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

$commonArgs = @(
  '--path', (Join-Path $root 'game'),
  '--rendering-method', $RenderingMethod,
  '--resolution', '1280x720',
  '--',
  '--rac1-iso', $Rac1Iso,
  '--gc-iso', $GcIso,
  '--uya-iso', $UyaIso,
  '--movement-smoke'
)

$destinations = @('rac1:LEVEL0', 'rac2:LEVEL1', 'rac3:TABLE1')
foreach ($destination in $destinations) {
  Write-Host "movement smoke: $destination" -ForegroundColor Cyan
  & $godot @commonArgs '--destination' $destination
  if ($LASTEXITCODE -ne 0) {
    Write-Error "movement smoke failed for $destination (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
  }
}

Write-Host "movement smoke: all representative trilogy worlds passed" -ForegroundColor Green
