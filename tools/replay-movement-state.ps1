#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$State,
    [ValidateRange(1, 3600)][int]$Frames = 180,
    [string]$Out,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [Alias('Profile')][switch]$CollectTimings,
    [switch]$Rendered,
    [ValidateSet('forward_plus', 'gl_compatibility', 'mobile')][string]$RenderingMethod = 'forward_plus',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $State) { $State = Join-Path $root 'captures/repros/quick-state.json' }
if (-not (Test-Path -LiteralPath $State -PathType Leaf)) { throw "Movement state not found: $State" }
if (-not $Out) { $Out = Join-Path $root 'captures/repros/forward-replay.json' }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
$replayArgs = @('--path', (Join-Path $root 'game'))
if ($Rendered) {
    # Keep the automated GPU replay off the desktop and unfocusable. Replays
    # bypass WindowPlacement, so these coordinates never change user preferences.
    $replayArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540',
        '--rendering-method', $RenderingMethod)
} else {
    $replayArgs += @('--headless', '--rendering-method', 'gl_compatibility')
}
$replayArgs += @(
    '--', '--load-movement-state', [IO.Path]::GetFullPath($State), '--replay-forward', $Frames,
    '--replay-out', [IO.Path]::GetFullPath($Out), '--fixed-seed', 1440)
if ($CollectTimings) { $replayArgs += '--replay-profile' }
if ($Rac1Iso) { $replayArgs += @('--rac1-iso', $Rac1Iso) }
& $godot @replayArgs
exit $LASTEXITCODE
