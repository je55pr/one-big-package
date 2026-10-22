#Requires -Version 5.1
<#
.SYNOPSIS
  Exercise ordinary-input recording/replay in separate Veldin and Novalis processes.
.DESCRIPTION
  No level solver, staging, progression events or observed-state injection.
  Also proves divergence reporting, explicit build overrides and incomplete-run rejection.
  Rendered mode uses unfocusable offscreen windows and preserves window preferences.
#>
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac1Iso -or -not (Test-Path -LiteralPath $Rac1Iso -PathType Leaf)) { throw 'A supported R&C1 ISO is required.' }
$output = Join-Path $root ('captures/playtest-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$built = [bool]$SkipBuild

function Test-Replay([string]$RunPath, [string]$Name, [int]$ExpectedExit, [string]$ExpectedResult, [switch]$AllowBuildChange) {
    $reportPath = Join-Path $output ($Name + '.report.json')
    & (Join-Path $PSScriptRoot 'replay-playtest.ps1') -Run $RunPath -Report $reportPath -Rac1Iso $Rac1Iso -SkipBuild -Rendered:$Rendered -AllowBuildChange:$AllowBuildChange | Out-Null
    if ($LASTEXITCODE -ne $ExpectedExit) { throw "$Name exit $LASTEXITCODE, expected $ExpectedExit. Inspect $reportPath.log" }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.Result -ne $ExpectedResult) { throw "$Name result $($report.Result), expected $ExpectedResult." }
    Write-Host "[playtest-smoke] $Name : $ExpectedResult" -ForegroundColor Green
    return $report
}

foreach ($level in @(0, 1)) {
    $runPath = Join-Path $output ("level$level.run.jsonl.gz")
    & (Join-Path $PSScriptRoot 'record-playtest.ps1') -Destination "rac1:LEVEL$level" -Out $runPath -Rac1Iso $Rac1Iso -Smoke -Headless:(-not $Rendered) -Offscreen:$Rendered -SkipBuild:$built | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Recording level $level failed. Inspect $runPath.log" }
    $built = $true
    $report = Test-Replay $runPath "level$level" 0 'matched'
    if ($report.Markers.Count -ne 1 -or $report.CheckpointsCompared -lt 10 -or $report.Steps -lt 900 -or $report.BuildChanged) {
        throw 'The ordinary input/marker/save sequence did not complete as expected.'
    }
}

# Modify observations/metadata only. The replay must compare them, never apply them.
function Write-Variant([string]$Kind) {
    $target = Join-Path $output ($Kind + '.run.jsonl.gz')
    $sourceFile = [IO.File]::OpenRead($runPath)
    $reader = [IO.StreamReader]::new([IO.Compression.GZipStream]::new($sourceFile, [IO.Compression.CompressionMode]::Decompress))
    $targetFile = [IO.File]::Create($target)
    $writer = [IO.StreamWriter]::new([IO.Compression.GZipStream]::new($targetFile, [IO.Compression.CompressionLevel]::Fastest), [Text.UTF8Encoding]::new($false))
    $changed = $false
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $entry = $line | ConvertFrom-Json
            if ($Kind -eq 'wrong-build' -and $entry.Header) {
                $entry.Header.AssemblyId = [guid]::NewGuid().ToString()
                $changed = $true
            }
            elseif ($Kind -eq 'divergent' -and -not $changed -and $entry.Step.Checkpoint) {
                $entry.Step.Checkpoint.Values.'player.position'.Numbers[0] += 10
                $changed = $true
            }
            elseif ($Kind -eq 'incomplete' -and $entry.End) { $changed = $true; continue }
            $writer.WriteLine(($entry | ConvertTo-Json -Depth 32 -Compress))
        }
    }
    finally { $writer.Dispose(); $reader.Dispose() }
    if (-not $changed) { throw "Variant $Kind was not generated." }
    return $target
}
$wrongBuild = Write-Variant 'wrong-build'
$invalid = Test-Replay $wrongBuild 'build-rejected' 2 'invalid'
if ($invalid.Error -notlike '*different build*') { throw 'Build mismatch was not explicitly rejected.' }
$allowed = Test-Replay $wrongBuild 'build-override' 0 'matched' -AllowBuildChange
if (-not $allowed.BuildChanged) { throw 'Build override was not identified in the report.' }
$divergent = Write-Variant 'divergent'
$different = Test-Replay $divergent 'state-difference' 3 'diverged'
if ($different.FirstDifference.Field -ne 'player.position' -or $different.Steps -ne $report.Steps) {
    throw 'Replay failed to report the changed observation and continue to the end.'
}
$incomplete = Write-Variant 'incomplete'
$invalid = Test-Replay $incomplete 'incomplete-rejected' 2 'invalid'
if ($invalid.Error -notlike '*incomplete*') { throw 'Missing completion record was not identified.' }
Write-Host "[playtest-smoke] PASS. Local evidence: $output" -ForegroundColor Green
