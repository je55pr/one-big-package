#Requires -Version 5.1
<#
.SYNOPSIS
  Launch OBP in interactive player mode for quick game-feel testing: load a
  Going Commando ISO, import a level, spawn the debug capsule. WASD / left
  stick move, mouse / right stick look, Space / south-face jump, C / right
  shoulder crouch, X / west-face action, F fly / noclip, R development respawn, F8 input
  diagnostics, Tab free cursor, Esc quit. The on-screen HUD reports
  position, speed and the last jump's air time / distance / apex.

.DESCRIPTION
  Builds game/ (Debug) so the latest code is used, then runs the pinned Godot
  builds against game/ and resolves the GC convenience args through the neutral
  destination/provider route. With no ISO available it falls back to Game Sources.

.EXAMPLE
  ./tools/play.ps1
  ./tools/play.ps1 -Rac2Iso "D:\isos\Going Commando.iso" -GcLevel 1
  ./tools/play.ps1 -SkipBuild
  ./tools/play.ps1 -Planet 2 -MaktarArena
  ./tools/play.ps1 -Planet 2 -MaktarArena -MaktarDispenser -MaktarDispenserInstance 394 -MaktarDispenserAnimate
#>
param(
  [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
  [int]$GcLevel = 1,
  [string]$Planet = '',
  [switch]$MaktarArena,
  [switch]$MaktarDispenser,
  [ValidateRange(394,399)][int]$MaktarDispenserInstance = 394,
  [switch]$MaktarDispenserAnimate,
  [string]$RenderingMethod = 'gl_compatibility',
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')

# --- resolve the disc image ---------------------------------------------------
if (-not $Rac2Iso -or -not (Test-Path $Rac2Iso)) {
  $candidates = @(
    $Rac2Iso,
    'C:\Claude\Inbox\Ratchet & Clank - Going Commando (USA) (v1.01).iso'
  ) | Where-Object { $_ -and (Test-Path $_) }
  $Rac2Iso = $candidates | Select-Object -First 1
}

if ($Rac2Iso) {
  if ($Planet) {
    Write-Host "OBP: straight to $Planet from $Rac2Iso" -ForegroundColor Cyan
    $sceneArgs = @('--rac2-iso', $Rac2Iso, '--planet', $Planet)
  } elseif ($PSBoundParameters.ContainsKey('GcLevel')) {
    Write-Host "OBP: straight to LEVEL$GcLevel from $Rac2Iso" -ForegroundColor Cyan
    $sceneArgs = @('--rac2-iso', $Rac2Iso, '--planet', "$GcLevel")
  } else {
    Write-Host "OBP: Worlds browser from $Rac2Iso" -ForegroundColor Cyan
    $sceneArgs = @('--rac2-iso', $Rac2Iso)
  }
} else {
  Write-Warning "No Going Commando ISO found (set OBP_GC_ISO or pass -Rac2Iso). Launching Game Sources."
  $sceneArgs = @('--test-scene', 'picker')
}

# An explicit debug entry uses actual Maktar floor collision and the regular
# shared player controller. It never grants normal campaign arena access.
if ($MaktarArena) {
  if (-not $Rac2Iso) { throw '-MaktarArena requires an authorised R&C2 source.' }
  if ($Planet -and $Planet -notin @('2', 'LEVEL2')) { throw '-MaktarArena is only available on LEVEL2.' }
  if (-not $Planet -and $PSBoundParameters.ContainsKey('GcLevel') -and $GcLevel -ne 2) {
    throw '-MaktarArena is only available on LEVEL2.'
  }
  $sceneArgs = @('--rac2-iso', $Rac2Iso, '--planet', '2',
    '--test-scene', 'player', '--maktar-arena-start')
}
if ($MaktarDispenser) {
  if (-not $MaktarArena) { throw '-MaktarDispenser requires -MaktarArena.' }
  $sceneArgs += @('--maktar-dispenser-preview', "$MaktarDispenserInstance")
  if ($MaktarDispenserAnimate) { $sceneArgs += '--maktar-dispenser-animate' }
} elseif ($MaktarDispenserAnimate) {
  throw '-MaktarDispenserAnimate requires -MaktarDispenser.'
}

# --- pinned Godot ------------------------------------------------------------
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $godot)) {
  & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}

# --- build + import --------------------------------------------------------
if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot 'build.ps1') -Game
  if ($LASTEXITCODE -ne 0) {
    Read-Host "`nBuild failed - press Enter to close"
    exit $LASTEXITCODE
  }
}
if (-not (Test-Path (Join-Path $root 'game/.godot'))) {
  & $godot --headless --path (Join-Path $root 'game') --import
}

# --- run --------------------------------------------------------------------
Write-Host "Controls: WASD/left stick move - mouse/right stick look - Space/south-face jump - C/right-shoulder crouch - X/west-face action - F fly - R development respawn - F8 input diagnostics - Tab cursor - Esc quit`n" -ForegroundColor DarkGray
$windowArgs = @(Get-ObpWindowArguments)
& $godot @windowArgs --path (Join-Path $root 'game') --rendering-method $RenderingMethod -- @sceneArgs
$code = $LASTEXITCODE
if ($code -ne 0) { Read-Host "`nGodot exited with $code - press Enter to close" }
exit $code
