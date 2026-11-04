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
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')
if (-not $State) { $State = Join-Path $root 'captures/repros/quick-state.json' }
if (-not (Test-Path -LiteralPath $State -PathType Leaf)) { throw "Movement state not found: $State" }
if (-not $Out) { $Out = Join-Path $root 'captures/repros/forward-replay.json' }
$State = [IO.Path]::GetFullPath($State)
$Out = [IO.Path]::GetFullPath($Out)
function Get-ObpMovementFileDigest {
    param([string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($Path))) }
    finally { $sha.Dispose() }
}
if ($State.Equals($Out, [StringComparison]::OrdinalIgnoreCase) -or
    ((Test-Path -LiteralPath $Out -PathType Leaf) -and
        (Get-ObpMovementFileDigest $State) -eq (Get-ObpMovementFileDigest $Out))) {
    throw 'Movement replay output must not overwrite the input state.'
}
if (Test-Path -LiteralPath $Out -PathType Container) { throw 'Movement replay output must be a file.' }
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
if (Test-Path -LiteralPath $Out -PathType Leaf) { Remove-Item -LiteralPath $Out -Force }
$code = Invoke-ObpSmokeProcess -FilePath $godot -ArgumentList $replayArgs -WorkingDirectory $root `
    -CompletionMarker "[movement-replay] PASS; frames=$Frames,"
if ($code -ne 0) { exit $code }

function Test-ObpMovementReplayNumber {
    param($Value)
    return ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) -and
        -not [double]::IsNaN([double]$Value) -and -not [double]::IsInfinity([double]$Value)
}
try {
    if (-not (Test-Path -LiteralPath $Out -PathType Leaf)) { throw 'Movement replay did not produce a fresh report.' }
    $report = Get-Content -LiteralPath $Out -Raw | ConvertFrom-Json
    $assembly = [guid]::Empty
    if ($report.StateFile -cne [IO.Path]::GetFileName($State) -or
        -not [guid]::TryParse([string]$report.HostAssemblyId, [ref]$assembly) -or $assembly -eq [guid]::Empty -or
        -not (Test-ObpMovementReplayNumber $report.PhysicsHz) -or $report.PhysicsHz -le 0) {
        throw 'Movement replay report lost its state/build/physics identity.'
    }
    $samples = @($report.Samples)
    if ($samples.Count -ne $Frames + 1) { throw 'Movement replay did not include its initial sample and every requested physics update.' }
    for ($tick = 0; $tick -lt $samples.Count; $tick++) {
        $sample = $samples[$tick]
        if (-not (Test-ObpMovementReplayNumber $sample.Tick) -or $sample.Tick -ne $tick) { throw 'Movement replay ticks are incomplete or out of order.' }
        foreach ($vector in @($sample.Position, $sample.Velocity)) {
            foreach ($axis in @('X', 'Y', 'Z')) {
                if (-not (Test-ObpMovementReplayNumber $vector.$axis)) { throw 'Movement replay has a missing or non-finite position/velocity coordinate.' }
            }
        }
        foreach ($angle in @($sample.FacingYaw, $sample.ControlHeading)) {
            if (-not (Test-ObpMovementReplayNumber $angle)) { throw 'Movement replay has a missing or non-finite heading.' }
        }
        if ($CollectTimings) {
            foreach ($field in @('ElapsedMs', 'RenderFrame', 'ControllerMs', 'ContactMs', 'SlideMs', 'SmallStepMs', 'CrestMs',
                'PresentationMs', 'CameraMs', 'EngineProcessMs', 'EnginePhysicsMs', 'RenderCpuMs', 'RenderGpuMs', 'Fps')) {
                if (-not (Test-ObpMovementReplayNumber $sample.Profile.$field)) { throw 'Movement replay is missing requested timing measurements.' }
            }
        }
    }
} catch {
    Write-ObpSmokeOutput $_.Exception.Message
    exit 2
}
exit 0
