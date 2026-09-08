#Requires -Version 5.1
<#
.SYNOPSIS
  Local screenshot regression check. Compares a directory of PNGs against a
  local baseline (MAE / windowed-SSIM / changed-pixel %) and writes diff heat
  images for any that fail.

.DESCRIPTION
  Baselines live in captures/baseline/<set>/ and are NEVER committed or uploaded
  (captures/ is gitignored) -- they are retail-derived. -Update refreshes the
  baseline from the current captures.

.EXAMPLE
  # after a shot run wrote captures/shots/*.png
  ./tools/vizcompare.ps1 -Current captures/shots -Set shots-rac2
  ./tools/vizcompare.ps1 -Current captures/shots -Set shots-rac2 -Update
#>
param(
  [Parameter(Mandatory)][string]$Current,
  [string]$Set = 'default',
  [string]$Baseline = '',
  [string]$Tolerances = 'tools/vizcompare/tolerances.json',
  [switch]$Update
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$curDir = Join-Path $root $Current
if (-not (Test-Path $curDir)) { Write-Error "no current captures: $curDir"; exit 2 }
if (-not $Baseline) { $Baseline = "captures/baseline/$Set" }
$baseDir = Join-Path $root $Baseline

if ($Update) {
  New-Item -ItemType Directory -Force -Path $baseDir | Out-Null
  Get-ChildItem $curDir -Filter *.png | ForEach-Object { Copy-Item $_.FullName $baseDir -Force }
  Write-Host "baseline updated: $baseDir ($((Get-ChildItem $baseDir -Filter *.png).Count) images)" -ForegroundColor Green
  exit 0
}

if (-not (Test-Path $baseDir) -or -not (Get-ChildItem $baseDir -Filter *.png -ErrorAction SilentlyContinue)) {
  Write-Warning "no baseline at $baseDir - run with -Update to create it, then review the images."
  exit 0
}

& python (Join-Path $PSScriptRoot 'vizcompare/compare.py') $baseDir $curDir `
  --tolerances (Join-Path $root $Tolerances) --diffdir (Join-Path $curDir '_diff')
exit $LASTEXITCODE
