#Requires -Version 5.1
<#
.SYNOPSIS
  Observe a human-played R&C1 progression sequence, then verify a fresh reload.
.DESCRIPTION
  Pass one starts a new isolated campaign on Veldin. Play to the Clank encounter,
  acknowledge the scene cards, collect 500 bolts and buy the Novalis Plumber's
  Infobot with Triangle. After unlocking Aridia, die/retry normally and travel
  Novalis -> Aridia -> Novalis through the planet map. The observer exits at PASS.
  Pass two reopens the resulting campaign and checks durable/idempotent progress.
  No input, unlock events, transforms, currency or developer travel are injected.
  Closing the window is cancellation. The save and logs remain under captures/.
#>
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$Godot,
    [string]$OutputDirectory,
    [switch]$SkipBuild,
    [switch]$Interactive
)
$ErrorActionPreference = 'Stop'
if (-not $Interactive) { throw 'This observer needs a human playthrough. Pass -Interactive to open its game window explicitly.' }
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) {
    throw 'An authorized supported R&C1 ISO is required.'
}
if (-not $Godot) {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
    $Godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
    if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) {
        & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
    }
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root ('captures/mission-play-' + [guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$state = Join-Path $OutputDirectory 'campaign.json'
if (Test-Path -LiteralPath $state) { throw 'Use a new output directory: this check must start without a campaign save.' }
foreach ($pass in 1..2) {
    $destination = if ($pass -eq 1) { 'rac1:LEVEL0' } else { 'rac1:LEVEL1' }
    $log = Join-Path $OutputDirectory "pass-$pass.log"
    Write-Host "Mission play pass $pass/2; save/logs: $OutputDirectory" -ForegroundColor Cyan
    & $Godot '--windowed' '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--log-file' $log '--' `
        '--rac1-iso' $Rac1Iso '--destination' $destination '--fixed-seed' '1440' `
        '--rac1-mission-play-smoke' '--no-profile' '--rac1-campaign-save' $state
    if ($LASTEXITCODE -ne 0) { throw "Mission play pass $pass failed (exit $LASTEXITCODE). See $log" }
    if (-not (Select-String -LiteralPath $log -SimpleMatch '[rac1-mission-play] PASS' -Quiet)) {
        throw "Mission play pass $pass was cancelled before PASS. See $log"
    }
}
Write-Host 'Mission play: human-input progression and fresh-process persistence passed.' -ForegroundColor Green
