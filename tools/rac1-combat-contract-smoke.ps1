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
  '--rac1-combat-contract-smoke'
)

Write-Host 'R&C1 synthetic combat host-contract smoke' -ForegroundColor Cyan
& $godot @godotArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
  Write-Error "R&C1 synthetic combat host-contract smoke failed (exit $code)."
  exit $code
}

Write-Host 'R&C1 synthetic combat host-contract smoke passed' -ForegroundColor Green
