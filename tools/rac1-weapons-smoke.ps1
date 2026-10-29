#Requires -Version 5.1
# Staged host checks, not a retail emulator replay. Rendered runs stay offscreen.
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [string]$CaptureDirectory,
    [switch]$SkipBuild,
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'

function Test-WeaponsSmokeLog([int]$exitCode, [string]$log) {
    # Godot can report a managed exception and still exit 0 after the fixture passes.
    # Inspect stderr as well as stdout before accepting the fixture's result.
    # Godot emits both plain ERROR lines and E <time> diagnostics. Managed
    # exceptions may be standalone or appear inside a timestamped engine line.
    $failure = '(?im)^\s*(?:(?:\[[^\]\r\n]+\]|20\d{2}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s*)*' +
        '(?:ERROR\s*:|SCRIPT ERROR\s*:|Unhandled\s+exception\b|FATAL(?:\s+ERROR)?\b|' +
        'E\s+\d+:\d{2}:\d{2}(?::\d+|\.\d+)?\s*:|' +
        '(?:System\.)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*Exception\s*:|' +
        '\[(?:weapons|arsenal)-smoke\]\s+FAIL\b)'
    return $exitCode -eq 0 -and
        $log.Contains('[lifecycle] quit-request reason=rac1-weapons-smoke-pass exitCode=0') -and
        -not [regex]::IsMatch($log, $failure)
}

if ($SelfTest) {
    $good = "[arsenal-smoke] PASS`n[lifecycle] quit-request reason=rac1-weapons-smoke-pass exitCode=0"
    if (-not (Test-WeaponsSmokeLog 0 $good)) { throw 'Clean arsenal completion was rejected.' }
    foreach ($problem in @(
        'ERROR: System.ArgumentException: Invalid skeletal layer sample.',
        'SCRIPT ERROR: Failed to load resource',
        'Unhandled exception. System.InvalidDataException',
        '2026-10-29 12:35:20 ERROR: Godot C# callback failed',
        '    [12:35:20] SCRIPT ERROR: invalid call',
        'E 0:00:04:123: Another engine error',
        'System.ArgumentException: Invalid skeletal layer sample.',
        '[weapons-smoke] FAIL: fixture assertion failed'
    )) {
        if (Test-WeaponsSmokeLog 0 "$good`n$problem") { throw "Exit-0 runtime error was accepted: $problem" }
    }
    if ((Test-WeaponsSmokeLog 1 $good) -or
        (Test-WeaponsSmokeLog 0 '[arsenal-smoke] PASS')) { throw 'Incomplete or failed run was accepted.' }
    Write-Output '[weapons-smoke] PASS log-gate self-test'
    exit 0
}
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'A supported R&C1 ISO is required.' }
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
[string[]]$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540') } else { @('--headless') }
$previousCaptureDirectory = $env:OBP_WEAPON_CAPTURE_DIR
$priorPreference = $ErrorActionPreference
$lines = [System.Collections.Generic.List[string]]::new()
try {
    if ($CaptureDirectory) { $env:OBP_WEAPON_CAPTURE_DIR = [IO.Path]::GetFullPath($CaptureDirectory) }
    # PowerShell 5.1 wraps native stderr as ErrorRecords. Treat it as captured
    # output rather than terminating the wrapper before the fixture has exited.
    $ErrorActionPreference = 'Continue'
    & $godot @windowArgs '--path' (Join-Path $root 'game') '--rendering-method' 'gl_compatibility' '--' '--rac1-iso' $Rac1Iso '--destination' 'rac1:LEVEL0' '--fixed-seed' '121' '--rac1-weapons-smoke' 2>&1 |
        ForEach-Object { $line = $_.ToString(); $lines.Add($line); Write-Output $line }
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $priorPreference
    $env:OBP_WEAPON_CAPTURE_DIR = $previousCaptureDirectory
}
if (-not (Test-WeaponsSmokeLog $code ($lines -join "`n"))) {
    Write-Output "[weapons-smoke] FAIL: fixture exit=$code or engine error/incomplete completion"
    exit 2
}
exit 0
