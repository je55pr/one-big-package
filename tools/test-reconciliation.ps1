#Requires -Version 5.1
<#
.SYNOPSIS
  Verify the shared player/profile/replay gate with authorised trilogy sources.
.DESCRIPTION
  Every engine process, including report replays, is headless and cannot capture
  the desktop pointer. Evidence stays in an ignored, unique captures directory.
  The JSON summary distinguishes automated proof from the separate human visual
  and audible playtest. It does not close the parent reconciliation issue.
#>
[CmdletBinding()]
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [string]$Out,
    [switch]$SkipTests,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($source in @($Rac1Iso, $GcIso, $UyaIso)) {
    if ([string]::IsNullOrWhiteSpace($source) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw 'Set OBP_RAC1_ISO, OBP_GC_ISO and OBP_UYA_ISO to authorised supported NTSC-U sources.'
    }
}
if (-not $Out) { $Out = Join-Path $root ('captures/reconciliation/' + [guid]::NewGuid().ToString('N')) }
$Out = [IO.Path]::GetFullPath($Out)
if (Test-Path -LiteralPath $Out) { throw 'Choose a new evidence directory so prior profiles and recordings cannot contaminate this gate.' }
New-Item -ItemType Directory -Path $Out | Out-Null
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
$results = New-Object System.Collections.Generic.List[object]
function Invoke-GateGame {
    param([string]$Name, [string[]]$Options, [string]$Expected)
    $arguments = @('--headless', '--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', '60',
        '--', '--rac1-iso', $Rac1Iso, '--gc-iso', $GcIso, '--uya-iso', $UyaIso, '--fixed-seed', '1440') + $Options
    $log = Join-Path $Out ($Name + '.log')
    $errorLog = Join-Path $Out ($Name + '.err.log')
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -StandardOutputPath $log -StandardErrorPath $errorLog -WorkingDirectory $root
    $passed = $code -eq 0 -and (Select-String -LiteralPath $log -SimpleMatch $Expected -Quiet)
    $results.Add([pscustomobject]@{ Phase = $Name; Passed = $passed; ExitCode = $code; Log = ($Name + '.log') })
    if (-not $passed) { throw "Reconciliation phase $Name failed (exit $code). Inspect its local logs." }
    Write-Host "$Name passed"
}
try {
    if (-not $SkipTests) {
        & (Join-Path $PSScriptRoot 'test-retail.ps1') -Rac1Iso $Rac1Iso -GcIso $GcIso -UyaIso $UyaIso -Configuration Release *> (Join-Path $Out 'dotnet-tests.log')
        if ($LASTEXITCODE -ne 0) { throw 'Retail and portable .NET tests failed.' }
        & dotnet format (Join-Path $root 'OneBigPackage.sln') --verify-no-changes --no-restore *> (Join-Path $Out 'format.log')
        if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }
        & (Join-Path $PSScriptRoot 'sync-godot-uids.ps1') -Check *> (Join-Path $Out 'uids.log')
        if ($LASTEXITCODE -ne 0) { throw 'Godot UID coverage failed.' }
        $results.Add([pscustomobject]@{ Phase = 'dotnet-retail-format-uids'; Passed = $true })
    }
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --no-restore -warnaserror *> (Join-Path $Out 'game-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
    }
    Invoke-GateGame 'native-rac1' @('--destination', 'rac1:LEVEL0', '--rac1-veldin-play-smoke') 'reason=rac1-veldin-play-smoke-pass'
    $legacy = Join-Path $Out 'legacy.run.jsonl.gz'
    Invoke-GateGame 'legacy-record' @('--destination', 'rac1:LEVEL0', '--record-playtest', $legacy, '--playtest-smoke') '[playtest] SAVED'
    Invoke-GateGame 'legacy-replay' @('--replay-playtest', $legacy, '--playtest-report', (Join-Path $Out 'legacy-replay.json')) '[playtest] PASS'
    Invoke-GateGame 'player-capabilities' @('--destination', 'rac1:LEVEL0', '--player-capabilities-smoke', '--player-capabilities-out', (Join-Path $Out 'poses.json')) '[player-capabilities-smoke] PASS'
    Invoke-GateGame 'trilogy-gameplay' @('--destination', 'rac1:LEVEL0', '--trilogy-gameplay-smoke') '[trilogy-gameplay-smoke] lease cleanup and persistent ammunition PASS'
    $profile = Join-Path $Out 'profile.json'
    Invoke-GateGame 'profile-write' @('--destination', 'rac1:LEVEL0', '--profile', $profile, '--profile-smoke', 'write', '--acceptance-out', (Join-Path $Out 'profile-write.json')) '[shared-profile-smoke] PASS'
    Invoke-GateGame 'profile-restart' @('--profile', $profile, '--profile-smoke', 'read', '--acceptance-out', (Join-Path $Out 'profile-restart.json')) '[shared-profile-smoke] PASS'
    $recording = Join-Path $Out 'route.run.jsonl.gz'
    Invoke-GateGame 'record-route' @('--destination', 'rac1:LEVEL0', '--record-playtest', $recording, '--reconciliation-smoke', '--acceptance-out', (Join-Path $Out 'capabilities-observed.json')) '[playtest] SAVED'
    Invoke-GateGame 'replay-route' @('--replay-playtest', $recording, '--playtest-report', (Join-Path $Out 'replay-route.json')) '[playtest] PASS'
    foreach ($world in @('rac2-LEVEL1', 'rac3-TABLE1', 'obp-fusion-lab')) {
        Invoke-GateGame ("bookmark-" + $world) @('--load-movement-state', (Join-Path ($recording + '.bookmarks') ($world + '.bookmark.json')), '--playtest-report', (Join-Path $Out ($world + '.replay.json'))) '[playtest] PASS'
        Invoke-GateGame ("bug-" + $world) @('--destination', ($world -replace '^(rac[23]|obp)-', '$1:'), '--bug-capture', '--bug-report-smoke', '--no-profile',
            '--bug-report-inbox', (Join-Path $Out ("inbox-" + $world))) '[bug-report-smoke] two fresh-process same-build replays through saved report markers PASS'
    }
    foreach ($file in @('legacy-replay.json', 'replay-route.json', 'rac2-LEVEL1.replay.json', 'rac3-TABLE1.replay.json', 'obp-fusion-lab.replay.json')) {
        $replay = Get-Content (Join-Path $Out $file) -Raw | ConvertFrom-Json
        if ($replay.Result -ne 'matched' -or $replay.BuildChanged -or $replay.CheckpointsCompared -le 0) { throw "Exact replay did not pass: $file" }
    }
    Copy-Item -LiteralPath (Join-Path $root 'tests/OBP.Tests/reconciliation-capabilities.json') -Destination (Join-Path $Out 'capabilities-declared.json')
    $status = if ($SkipTests -or $SkipBuild) { 'partial' } else { 'passed' }
}
catch { $status = 'failed'; $failure = $_.Exception.Message; throw }
finally {
    [ordered]@{ Version = 1; Result = $status; Failure = $failure; Headless = $true;
        HumanVisualAndAudibleReview = 'not performed by this script'; Phases = @($results.ToArray()) } |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Out 'summary.json') -Encoding UTF8
    Write-Host "Reconciliation evidence: $Out"
}
