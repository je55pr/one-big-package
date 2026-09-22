#Requires -Version 5.1
<#
.SYNOPSIS
  Run the honest ordinary-play R&C1 Veldin smoke.

.DESCRIPTION
  Builds the Godot host, starts rac1:LEVEL0 through the normal source/destination
  provider path with an ephemeral campaign session, then drives only ordinary
  InputMap/keyboard controls. The gate verifies the clean opening inventory,
  usable Bomb Glove ammo, recovered horizontal and vertical camera input,
  visible hostile motion, a natural hostile attack/damage event, practical
  hostile/crate wrench contacts, and a natural fall/death/respawn.

  This smoke never stages Ratchet or enemy transforms, injects checkpoint/death/
  attack state, mutates ammo to manufacture success, or calls gameplay
  consequence handlers directly.
#>
param(
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$RenderingMethod = 'gl_compatibility',
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
  Write-Error 'A supported R&C1 ISO is required. Pass -Rac1Iso or set OBP_RAC1_ISO.'
  exit 2
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

$godotArgs = @(
  '--path', (Join-Path $root 'game'),
  '--rendering-method', $RenderingMethod,
  '--resolution', '1280x720',
  '--',
  '--rac1-iso', $Rac1Iso,
  '--destination', 'rac1:LEVEL0',
  '--rac1-veldin-play-smoke'
)

Write-Host 'R&C1 Veldin ordinary-play smoke' -ForegroundColor Cyan
& $godot @godotArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
  Write-Error "R&C1 Veldin ordinary-play smoke failed (exit $code)."
  exit $code
}

Write-Host 'R&C1 Veldin ordinary-play smoke passed' -ForegroundColor Green
