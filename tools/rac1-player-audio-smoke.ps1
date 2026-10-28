#Requires -Version 5.1
# Staged input and playback checks. Rendered runs stay unfocusable and offscreen.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso) -SourceCount 1 -SkipBuild:$SkipBuild
[string[]]$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540') } else { @('--headless') }
& $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL0' '--fixed-seed' '121' '--rac1-player-audio-smoke'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Static-camera capture deliberately has no player. Diagnostic construction must
# report that unavailable fixture instead of dereferencing an unbound audio owner.
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$logs = Join-Path $root 'captures/rac1-player-audio-smoke'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$stdout = Join-Path $logs 'static-camera.stdout.log'
$stderr = Join-Path $logs 'static-camera.stderr.log'
$arguments = @('--headless', '--fixed-fps', '60', '--path', (Join-Path $root 'game'),
    '--rendering-method', 'gl_compatibility', '--', '--rac1-iso', $Rac1Iso,
    '--destination', 'rac1:LEVEL0', '--test-scene', 'worlds', '--capture-frame', '120', '--rac1-player-audio-smoke')
$code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $stdout -StandardErrorPath $stderr
$log = (Get-Content -LiteralPath $stdout -Raw) + (Get-Content -LiteralPath $stderr -Raw)
if ($code -ne 1 -or -not $log.Contains("The audio fixture's player is unavailable.") -or
    -not $log.Contains('reason=rac1-player-audio-smoke-fail exitCode=1') -or
    $log -match '(?im)NullReferenceException|Object reference not set|^ERROR:|^SCRIPT ERROR:|ObjectDB instances were leaked|resources still in use|quit preparation failed') {
    throw "Static-camera audio availability regression failed (exit=$code). Inspect $logs"
}
Write-Host '[player-audio-smoke] static-camera unavailable fixture is explicit and shuts down cleanly PASS'
exit 0
