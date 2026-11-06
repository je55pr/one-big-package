#Requires -Version 5.1
# Retail-backed Godot exit regression. Godot often exits code 0 even when ObjectDB leaks.
# No ISO, decoded audio or local paths are written to the repository.
param(
    [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GodotPath,
    [ValidateRange(1, 20)][int]$NovalisRepeats = 3,
    [switch]$SkipBuild,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Test-AudioExitLog([int]$exitCode, [string]$log, [string]$completionMarker) {
    # Inspect the complete process output, including warnings after root-exit.
    $bad = '(?im)^Leaked instance:|^WARNING:.*(?:ObjectDB instances were leaked|resources still in use at exit)|^ERROR:|^Unhandled exception'
    return $exitCode -eq 0 -and $log.Contains('[lifecycle] root-exit') -and
        $log.Contains($completionMarker) -and -not [regex]::IsMatch($log, $bad)
}

if ($SelfTest) {
    $marker = '[fixture] PASS'
    $clean = "$marker`n[lifecycle] root-exit"
    if (-not (Test-AudioExitLog 0 $clean $marker)) { throw 'Clean log rejected.' }
    foreach ($warning in @(
        'WARNING: 16 ObjectDB instances were leaked at exit (run with --verbose for details).',
        'Leaked instance: AudioStreamPlaybackWAV:123 - Reference count: 1',
        'WARNING: 1 resources still in use at exit.',
        'ERROR: shutdown failed', 'Unhandled exception. System.InvalidOperationException'
    )) {
        if (Test-AudioExitLog 0 "$clean`n$warning" $marker) { throw "Exit-0 failure accepted: $warning" }
    }
    if ((Test-AudioExitLog 1 $clean $marker) -or
        (Test-AudioExitLog 0 '[lifecycle] root-exit' $marker) -or
        (Test-AudioExitLog 0 $marker $marker)) { throw 'Incomplete or failed probe accepted.' }
    Write-Host '[audio-shutdown-smoke] PASS log-gate self-test (exit-0 leaks, errors, missing completion/root exit)'
    exit 0
}

$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
if (-not $GodotPath) {
    $GodotPath = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Pinned Godot executable unavailable. Run tools/bootstrap-godot.ps1 or supply -GodotPath.'
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { throw 'The Godot C# build failed.' }
}

$sourceArgs = @()
if ($Rac2Iso) {
    if (-not (Test-Path -LiteralPath $Rac2Iso -PathType Leaf)) {
        throw 'The selected R&C2 ISO is not accessible.'
    }
    $sourceArgs += @('--rac2-iso', $Rac2Iso)
}
$logDirectory = Join-Path $root 'captures/audio-shutdown-smoke'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Invoke-AudioExitProbe(
    [string]$label, [string[]]$gameArgs, [int]$expectedWorlds, [bool]$expectStress,
    [string[]]$sources = $sourceArgs, [string]$completionMarker = '[lifecycle] root-exit'
) {
    $arguments = @(
        '--verbose', '--headless', '--path', (Join-Path $root 'game'),
        '--rendering-method', 'gl_compatibility',
        '--quit-after', '3600', '--'
    ) + $sources + $gameArgs

    # Capture stderr too: Godot prints ObjectDB leaks there but returns success.
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $GodotPath @arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    $log = $lines -join "`n"
    $logFile = Join-Path $logDirectory (($label.ToLowerInvariant() -replace '[^a-z0-9]+', '-') + '.log')
    Set-Content -LiteralPath $logFile -Value $lines -Encoding UTF8
    $worlds = [regex]::Matches($log, '\[destinations\] world ready').Count
    $audio = [regex]::Matches($log, '\[audio\] playing [1-9][0-9]* level audio source\(s\)').Count
    $cleanExit = Test-AudioExitLog $exitCode $log $completionMarker
    $stressFinished = $log.Contains('[stress] ================ summary ================')

    if (-not $cleanExit -or $worlds -lt $expectedWorlds -or $audio -lt $expectedWorlds -or
        ($expectStress -and -not $stressFinished)) {
        $relevant = $lines | Where-Object {
            $_ -match 'Leaked instance|ObjectDB instances were leaked|resources still in use|^ERROR:|^Unhandled exception|\[audio\] playing|\[destinations\] world ready|\[stress\]|\[lifecycle\]|\[novalis-'
        }
        $relevant | ForEach-Object { Write-Host $_ }
        throw "FAIL $label (exit=$exitCode worlds=$worlds audio=$audio cleanExit=$cleanExit stressFinished=$stressFinished). Full log: $logFile"
    }
    Write-Host "[audio-shutdown-smoke] PASS $label ($worlds worlds, $audio level-audio starts; no ObjectDB leaks)"
}

# Even the one-world probe requests an ordinary application quit; --quit-after
# is only a watchdog, since it bypasses application teardown preparation.
Invoke-AudioExitProbe 'Oozla shutdown' @('--stress-switch', 'oozla') 1 $true $sourceArgs '[lifecycle] audio retired before quit'
# Repeatedly unload and reload the same live audio world through the normal host path.
Invoke-AudioExitProbe 'three Oozla world switches' @('--stress-switch', 'oozla,oozla,oozla') 3 $true $sourceArgs '[lifecycle] audio retired before quit'
if ($Rac1Iso) {
    if (-not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'The selected R&C1 ISO is not accessible.' }
    $novalisSources = @('--rac1-iso', $Rac1Iso)
    for ($iteration = 1; $iteration -le $NovalisRepeats; $iteration++) {
        foreach ($fixture in @('route', 'swim')) {
            $marker = "[lifecycle] quit-request reason=rac1-novalis-$fixture-smoke-pass exitCode=0"
            $fixtureArgs = @('--destination', 'rac1:LEVEL1', '--fixed-seed', '121', "--rac1-novalis-$fixture-smoke")
            Invoke-AudioExitProbe "Novalis $fixture $iteration" $fixtureArgs 1 $false $novalisSources $marker
        }
    }
} else {
    Write-Host '[audio-shutdown-smoke] Novalis not exercised: supply -Rac1Iso or OBP_RAC1_ISO.'
}
Write-Host '[audio-shutdown-smoke] PASS'
