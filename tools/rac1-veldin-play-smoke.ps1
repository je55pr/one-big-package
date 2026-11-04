#Requires -Version 5.1
<#
.SYNOPSIS
  Run the honest ordinary-play R&C1 Veldin smoke.

.DESCRIPTION
  Builds the Godot host, starts rac1:LEVEL0 through the normal source/destination
  provider path with an ephemeral campaign session, then drives only ordinary
  InputMap/keyboard controls. The gate verifies the clean opening inventory,
  usable Bomb Glove ammo, recovered horizontal and vertical camera input,
  all 16 Veldin class-749 placements idle at the authored start, polygon-driven
  population activation, a natural hostile attack/damage event, practical
  hostile/crate wrench contacts, and a natural fall/death/automatic-restart
  path that requires no development R-key respawn input.

  This smoke never stages Ratchet or enemy transforms, injects checkpoint/death/
  attack state, mutates ammo to manufacture success, presses the development
  respawn control, or calls gameplay consequence handlers directly.
#>
param(
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$RenderingMethod = 'gl_compatibility',
  [int]$Seed = 1440,
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')

if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
  Write-Error 'A supported R&C1 ISO is required. Pass -Rac1Iso or set OBP_RAC1_ISO.'
  exit 2
}

$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild

$godotArgs = @(Get-ObpWindowArguments) + @(
  '--path', (Join-Path $root 'game'),
  '--rendering-method', $RenderingMethod,
  '--',
  '--rac1-iso', $Rac1Iso,
  '--destination', 'rac1:LEVEL0',
  '--fixed-seed', $Seed,
  '--rac1-veldin-play-smoke'
)

Write-Host 'R&C1 Veldin ordinary-play smoke' -ForegroundColor Cyan
$code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList $godotArgs `
  -CompletionMarker '[lifecycle] quit-request reason=rac1-veldin-play-smoke-pass exitCode=0' -WorkingDirectory $root
if ($code -ne 0) {
  Write-Error "R&C1 Veldin ordinary-play smoke failed (exit $code)." -ErrorAction Continue
  exit $code
}

Write-Host 'R&C1 Veldin ordinary-play smoke passed' -ForegroundColor Green
