#Requires -Version 5.1
<#
.SYNOPSIS
  Deterministic R&C1 LEVEL0 vs UYA TABLE1 Fusion Lab comparison capture.
.DESCRIPTION
  Captures each retail start area independently, then translation-only start-anchored
  overlay and side-by-side views. No semantic landmark or whole-map transform is solved.
#>
[CmdletBinding()]
param(
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$UyaIso = $env:OBP_UYA_ISO,
  [string]$OutDir = '',
  [int]$Frame = 120,
  [int]$ReloadCycles = 3,
  [string]$Resolution = '1280x720'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$preset = Join-Path $root 'compositions/veldin-rac1-uya-comparison.json'

foreach ($source in @(
  @{ label='R&C1'; path=$Rac1Iso },
  @{ label='UYA'; path=$UyaIso }
)) {
  if ([string]::IsNullOrWhiteSpace($source.path) -or -not (Test-Path -LiteralPath $source.path -PathType Leaf)) {
    throw "$($source.label) retail ISO is required and must be readable."
  }
}
if (-not $OutDir) {
  $OutDir = Join-Path $root 'captures/veldin-rac1-uya'
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$views = @(
  @{ file='rac1-start.png'; view='a-start' },
  @{ file='uya-start.png'; view='b-start' },
  @{ file='start-overlay.png'; view='start-overlay' },
  @{ file='start-side-by-side.png'; view='start-side-by-side' }
)

foreach ($shot in $views) {
  $out = Join-Path $OutDir $shot.file
  & (Join-Path $PSScriptRoot 'capture.ps1') `
    -Composition $preset -CompositionView $shot.view `
    -Rac1Iso $Rac1Iso -UyaIso $UyaIso `
    -Frame $Frame -Resolution $Resolution -Out $out -SkipBuild
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($ReloadCycles -gt 0) {
  $manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
  $godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
  if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
  }

  & $godot --headless --path (Join-Path $root 'game') -- `
    --compose --composition $preset `
    --rac1-iso $Rac1Iso --uya-iso $UyaIso `
    --compose-reload $ReloadCycles
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Veldin comparison captures: $OutDir"
Write-Host 'Placement contract: identity preset; start-* views use translation-only RuntimeWorld ship/start anchoring.'
