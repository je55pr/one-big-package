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
  [switch]$SkipBuild,
  [switch]$Render
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

$statePath = Join-Path ([IO.Path]::GetTempPath()) (
  'obp-rac1-campaign-' + [guid]::NewGuid().ToString('N') + '.json')

$commonArgs = @(
  '--path', (Join-Path $root 'game'),
  '--rendering-method', $RenderingMethod,
  '--',
  '--rac1-iso', $Rac1Iso,
  '--rac1-campaign-smoke',
  '--no-profile',
  '--rac1-campaign-save', $statePath
)

try {
  foreach ($pass in 1..2) {
    Write-Host "R&C1 campaign smoke pass $pass/2" -ForegroundColor Cyan
    $windowArgs = if ($Render) { @(Get-ObpWindowArguments) } else { @('--headless') }
    $code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList (@($windowArgs) + $commonArgs) `
      -CompletionMarker '[lifecycle] quit-request reason=rac1-campaign-smoke-pass exitCode=0' -WorkingDirectory $root
    if ($code -ne 0) {
      Write-Error "R&C1 campaign smoke pass $pass failed (exit $code)." -ErrorAction Continue
      exit $code
    }
  }

  Write-Host 'R&C1 campaign smoke: both process passes passed' -ForegroundColor Green
}
finally {
  Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath ($statePath + '.tmp') -Force -ErrorAction SilentlyContinue
}
