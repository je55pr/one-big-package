#Requires -Version 5.1
<#
.SYNOPSIS
  Run the lower-level synthetic R&C1 combat host-contract smoke.

.DESCRIPTION
  Builds the Godot host and runs the explicitly synthetic contract harness.
  Unlike tools/rac1-veldin-play-smoke.ps1, this harness is allowed to stage
  transforms and invoke narrow internal seams to isolate host collision,
  presentation, Veldin population gating and unload/reload contracts. Its pass result is
  not evidence of ordinary-play reachability or end-to-end playability.
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
  '--rac1-combat-contract-smoke'
)

Write-Host 'R&C1 synthetic combat host-contract smoke' -ForegroundColor Cyan
$code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList $godotArgs `
  -CompletionMarker '[lifecycle] quit-request reason=rac1-combat-contract-pass exitCode=0' -WorkingDirectory $root
if ($code -ne 0) {
  Write-Error "R&C1 synthetic combat host-contract smoke failed (exit $code)." -ErrorAction Continue
  exit $code
}

Write-Host 'R&C1 synthetic combat host-contract smoke passed' -ForegroundColor Green
