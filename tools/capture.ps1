#Requires -Version 5.1
<#
.SYNOPSIS
  Deterministic Godot capture: build -> launch -> screenshot -> exit.
.EXAMPLE
  ./tools/capture.ps1 smoke
  ./tools/capture.ps1 -Picker -Rac1Iso "D:\isos\RAC1.iso" -GcIso "D:\isos\GC.iso" -UyaIso "D:\isos\UYA.iso"
  ./tools/capture.ps1 -Worlds -GcIso "D:\isos\GC.iso"
  ./tools/capture.ps1 -Destination rac2:LEVEL1 -GcIso "D:\isos\GC.iso" -Frame 120
#>
param(
  [Parameter(Position = 0)][string]$Scene = 'smoke',
  [int]$Frame = 30,
  [string]$Out = '',
  [string]$RenderingMethod = 'gl_compatibility',
  [string]$Resolution = '1280x720',
  [string]$Rac1Iso = $env:OBP_RAC1_ISO,
  [string]$GcIso = $env:OBP_GC_ISO,
  [string]$UyaIso = $env:OBP_UYA_ISO,
  [int]$GcLevel = 1,
  [string]$Destination = '',
  [switch]$Picker,
  [switch]$Worlds,
  [switch]$Player,
  [switch]$DiagnosticPlayer,
  [switch]$VerifyHash,
  [switch]$CollisionDebug,
  [switch]$CrateFocus,
  [switch]$CrateAutoStrike,
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Picker) { $Scene = 'picker' }
if ($Worlds) { $Scene = 'worlds' }
if ($Player) { $Scene = 'player'; if ($PSBoundParameters['Frame'] -eq $null) { $Frame = 260 } }
if ($CrateFocus -or $CrateAutoStrike) { $Scene = 'player'; if ($PSBoundParameters['Frame'] -eq $null) { $Frame = 120 } }

$sourceArgs = @()
foreach ($source in @(
  @{ option='--rac1-iso'; path=$Rac1Iso; label='R&C1' },
  @{ option='--gc-iso'; path=$GcIso; label='GC' },
  @{ option='--uya-iso'; path=$UyaIso; label='UYA' }
)) {
  if ($source.path) {
    if (-not (Test-Path -LiteralPath $source.path -PathType Leaf)) {
      Write-Error "$($source.label) ISO not found: $($source.path)"
      exit 2
    }
    $sourceArgs += @($source.option, $source.path)
  }
}

if ($Destination) {
  $sourceArgs += @('--destination', $Destination)
} elseif ($GcIso) {
  # Preserve the established GC capture/player convenience. --gc-level is
  # ignored by picker/worlds screens but resolves through the neutral destination
  # route for direct player/showcase captures.
  $sourceArgs += @('--gc-level', "$GcLevel")
}
if ($VerifyHash) { $sourceArgs += '--verify-hash' }
if ($DiagnosticPlayer) { $sourceArgs += '--diagnostic-player' }
if ($CollisionDebug) { $sourceArgs += '--collision-debug' }
if ($CrateFocus) { $sourceArgs += '--crate-focus' }
if ($CrateAutoStrike) { $sourceArgs += '--crate-auto-strike' }

if (-not $Out) {
  $name = if ($Destination) {
    'destination-' + ($Destination -replace '[^A-Za-z0-9_-]', '-')
  } elseif ($Worlds) {
    'worlds'
  } elseif ($Picker) {
    'sources'
  } elseif ($Player -and $GcIso) {
    "gc-player-l$GcLevel"
  } elseif ($GcIso) {
    "gc-level$GcLevel"
  } else {
    $Scene
  }
  $Out = Join-Path $root "captures/$name.png"
}
$Out = [System.IO.Path]::GetFullPath($Out)

$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }

if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot 'build.ps1') -Game
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not (Test-Path (Join-Path $root 'game/.godot'))) {
  & $godot --headless --path (Join-Path $root 'game') --import
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
& $godot --path (Join-Path $root 'game') --rendering-method $RenderingMethod --resolution $Resolution `
  -- --test-scene $Scene --capture-frame $Frame --capture-out $Out @sourceArgs
$code = $LASTEXITCODE
if ($code -eq 0 -and (Test-Path $Out)) { Write-Host "capture: $Out" } else { Write-Error "capture failed ($code)" }
exit $code
