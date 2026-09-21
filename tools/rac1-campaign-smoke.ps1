#Requires -Version 5.1
<#
.SYNOPSIS
  Run the bounded R&C1 campaign travel/revisit persistence smoke.

.DESCRIPTION
  Builds the Godot host once, then launches it twice against an isolated temporary
  campaign-state file. Pass one starts from the recovered opening campaign state,
  injects only the proven destination-discovery dispatcher events 0x25 and 0x26,
  travels 0 -> 1 -> 2, explicitly injects the retained level-2 checkpoint/death
  witness, reloads level 2, returns to 1, and persists. Pass two starts a fresh
  process, restores CurrentLevel/unlock order, and revisits 1 -> 2 -> 1.

  The dispatcher values and level-2 checkpoint activation/death are test inputs,
  not claims about unrecovered mission/objective/checkpoint trigger producers.
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

$statePath = Join-Path ([IO.Path]::GetTempPath()) (
  'obp-rac1-campaign-' + [guid]::NewGuid().ToString('N') + '.json')

$commonArgs = @(
  '--path', (Join-Path $root 'game'),
  '--rendering-method', $RenderingMethod,
  '--resolution', '1280x720',
  '--',
  '--rac1-iso', $Rac1Iso,
  '--rac1-campaign-smoke',
  '--rac1-campaign-save', $statePath
)

try {
  foreach ($pass in 1..2) {
    Write-Host "R&C1 campaign smoke pass $pass/2" -ForegroundColor Cyan
    & $godot @commonArgs
    if ($LASTEXITCODE -ne 0) {
      Write-Error "R&C1 campaign smoke pass $pass failed (exit $LASTEXITCODE)."
      exit $LASTEXITCODE
    }
  }

  Write-Host 'R&C1 campaign smoke: both process passes passed' -ForegroundColor Green
}
finally {
  Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath ($statePath + '.tmp') -Force -ErrorAction SilentlyContinue
}
