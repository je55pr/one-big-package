#Requires -Version 5.1
<#
.SYNOPSIS
  Exercise the complete player bug-report flow and its separate-process replays.
.DESCRIPTION
  Uses ordinary authored level entry and logical keyboard/controller input. No
  transforms, grants, injected objectives or recorded observation restoration.
  -Rendered tests the actual OpenGL UI offscreen without stealing focus.
#>
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$CampaignSave,
    [ValidatePattern('^rac1:LEVEL([0-9]|1[0-8])$')][string]$Destination = 'rac1:LEVEL0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) { throw 'Prepare the pinned Godot toolchain first.' }
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'Supply an authorized R&C1 ISO.' }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $godot --headless --editor --import --path (Join-Path $root 'game') --quit
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$name = 'smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$inbox = Join-Path $root "captures/bug-reports/$name"
New-Item -ItemType Directory -Path $inbox -Force | Out-Null
$log = Join-Path $inbox 'smoke.log'
$engineArgs = @('--verbose', '--path', (Join-Path $root 'game'), '--fixed-fps', '60')
if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540', '--rendering-method', 'gl_compatibility') }
else { $engineArgs += '--headless' }
$gameArgs = @('--', '--rac1-iso', $Rac1Iso, '--destination', $Destination, '--bug-capture', '--bug-report-smoke', '--bug-report-inbox', $inbox)
if ($CampaignSave) { $gameArgs += @('--rac1-campaign-save', [IO.Path]::GetFullPath($CampaignSave)) }
& $godot @engineArgs @gameArgs > $log 2>&1
$code = $LASTEXITCODE
$output = Get-Content -LiteralPath $log -Raw
if ($code -ne 0 -or -not $output.Contains('reason=bug-report-smoke-pass') -or
    $output -match '(?im)^ERROR:|^Unhandled exception|ObjectDB instances were leaked|^Leaked instance:') {
    Get-Content -LiteralPath $log -Tail 70
    throw "Bug report smoke failed (exit=$code). Log: $log"
}
$results = @(Get-ChildItem (Join-Path $inbox 'replay-results') -Filter '*.json')
if ($results.Count -ne 2) { throw 'Both fresh-process replay results are required.' }
foreach ($file in $results) {
    $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($result.Result -ne 'matched' -or $result.BuildChanged -or -not $result.ReportMarker -or
        $result.Steps -ne ($result.ReportMarker.Step + 1)) { throw 'Replay did not match at the exact selected report marker.' }
}
# Closing the capture must also finalize its canonical tape, including the
# history after the last saved report. Exercise it with the original replayer.
$live = @(Get-ChildItem (Join-Path $inbox 'live') -Filter 'session.run.jsonl.gz' -Recurse)
if ($live.Count -ne 1) { throw 'Closing did not finalize the canonical recording.' }
$fullResult = Join-Path $inbox 'full-tape-replay.json'
& (Join-Path $PSScriptRoot 'replay-playtest.ps1') -Run $live[0].FullName -Report $fullResult -Rac1Iso $Rac1Iso -SkipBuild *> (Join-Path $inbox 'full-tape-replay.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $inbox 'full-tape-replay.log') -Tail 30; throw 'Finalized canonical recording did not replay.' }
$full = Get-Content -LiteralPath $fullResult -Raw | ConvertFrom-Json
if ($full.Result -ne 'matched' -or $full.BuildChanged) { throw 'Canonical tape diverged or needed a build override.' }
# The human inbox replay stays paused at its marker until the window closes.
$sessionFile = @(Get-ChildItem $inbox -Filter 'session.json' -Recurse)
if ($sessionFile.Count -ne 1) { throw 'Expected one published reporting session.' }
$session = Get-Content -LiteralPath $sessionFile[0].FullName -Raw | ConvertFrom-Json
$inspectionResult = Join-Path $inbox 'inspection-replay.json'
$inspectionLog = Join-Path $inbox 'inspection-replay.log'
$inspectionArgs = @('--headless', '--path', (Join-Path $root 'game'), '--fixed-fps', '60', '--script', 'res://tests/bug_inspect_smoke.gd', '--',
    '--rac1-iso', $Rac1Iso, '--replay-bug-report', $sessionFile[0].FullName, '--bug-report-id', $session.Reports[0].Id,
    '--playtest-inspect-marker', '--playtest-report', $inspectionResult)
& $godot @inspectionArgs > $inspectionLog 2>&1
$inspectionCode = $LASTEXITCODE
$inspectionOutput = Get-Content -LiteralPath $inspectionLog -Raw
if ($inspectionCode -ne 0 -or -not $inspectionOutput.Contains('[bug-inspect-smoke] PASS') -or
    $inspectionOutput -match '(?im)^ERROR:|^Unhandled exception|ObjectDB instances were leaked|^Leaked instance:') {
    Get-Content -LiteralPath $inspectionLog -Tail 30
    throw 'Interactive replay did not retain and close its paused marked scene cleanly.'
}
Write-Host "[bug-report-smoke] PASS $Destination (2 selected-marker replays, finalized canonical replay, paused inspection). Local artifacts: $inbox"
