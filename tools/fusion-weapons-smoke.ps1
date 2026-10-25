#Requires -Version 5.1
<#
.SYNOPSIS
  Three-source Veldin weapon fixtures and a fresh-process ordinary input replay.
.DESCRIPTION
  Staged contacts exercise original authored crates/robots; the recording starts
  at the ordinary Veldin entry and uses input only. Captures and tapes stay local.
#>
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($sourcePath in @($Rac1Iso, $GcIso, $UyaIso)) {
    if (-not $sourcePath -or -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw 'All three authorised NTSC-U sources are required.' }
}
if (-not $Out) { $Out = Join-Path $root ('captures/fusion-' + [guid]::NewGuid().ToString('N')) }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OBP.Game.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $godot --headless --editor --path (Join-Path $root 'game') --quit | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$windowArgs = if ($Rendered) { @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') } else { @('--headless') }
$previousCapture = $env:OBP_FUSION_CAPTURE_DIR
try {
    if ($Rendered) { $env:OBP_FUSION_CAPTURE_DIR = $Out }
    $integrationLog = Join-Path $Out 'integration.log'
    & $godot @windowArgs --fixed-fps 60 --path (Join-Path $root 'game') --rendering-method gl_compatibility --log-file $integrationLog -- --fusion-smoke --fixed-seed 1440 --rac1-iso $Rac1Iso --gc-iso $GcIso --uya-iso $UyaIso
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath $integrationLog -SimpleMatch '[fusion-smoke] PASS:')) { throw "Fusion fixtures failed. Inspect $integrationLog" }
    $run = Join-Path $Out 'ordinary.run.jsonl.gz'
    & (Join-Path $PSScriptRoot 'record-playtest.ps1') -Destination rac1:LEVEL0 -FusionTest -Smoke -Out $run -Rac1Iso $Rac1Iso -GcIso $GcIso -UyaIso $UyaIso -Headless:(-not $Rendered) -Offscreen:$Rendered -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw 'Ordinary fusion recording failed.' }
    $reportPath = Join-Path $Out 'replay.report.json'
    & (Join-Path $PSScriptRoot 'replay-playtest.ps1') -Run $run -Report $reportPath -Rac1Iso $Rac1Iso -GcIso $GcIso -UyaIso $UyaIso -Rendered:$Rendered -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw 'Fusion replay failed.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.Result -ne 'matched' -or $report.Markers.Count -ne 3 -or $report.CheckpointsCompared -lt 10) { throw 'Fusion replay observations/markers were incomplete.' }

    # A cross-build override must never override a changed source identity.
    $wrongSource = Join-Path $Out 'wrong-source.run.jsonl.gz'
    $reader = [IO.StreamReader]::new([IO.Compression.GZipStream]::new([IO.File]::OpenRead($run), [IO.Compression.CompressionMode]::Decompress))
    $writer = [IO.StreamWriter]::new([IO.Compression.GZipStream]::new([IO.File]::Create($wrongSource), [IO.Compression.CompressionLevel]::Fastest), [Text.UTF8Encoding]::new($false))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $entry = $line | ConvertFrom-Json
            if ($entry.Header) { $entry.Header.Weapons.Sources.rac2 = 'wrong-source-build' }
            $writer.WriteLine(($entry | ConvertTo-Json -Depth 32 -Compress))
        }
    }
    finally { $writer.Dispose(); $reader.Dispose() }
    $invalidReport = Join-Path $Out 'wrong-source.report.json'
    & (Join-Path $PSScriptRoot 'replay-playtest.ps1') -Run $wrongSource -Report $invalidReport -Rac1Iso $Rac1Iso -GcIso $GcIso -UyaIso $UyaIso -Rendered:$Rendered -SkipBuild -AllowBuildChange
    if ($LASTEXITCODE -ne 2) { throw "Expected source-rejection exit code 2, got $LASTEXITCODE. Inspect $invalidReport" }
    $invalid = Get-Content -LiteralPath $invalidReport -Raw | ConvertFrom-Json
    $expectedError = 'Recorded cross-game weapon sources do not match (rac2). Attach the exact original sources used by the recording.'
    if ($invalid.Result -ne 'invalid' -or $invalid.Error -ne $expectedError) { throw 'Replay did not identify the exact cross-game source blocker.' }
}
finally { $env:OBP_FUSION_CAPTURE_DIR = $previousCapture }
foreach ($log in Get-ChildItem -LiteralPath $Out -Filter '*.log') {
    if (Select-String -LiteralPath $log.FullName -Pattern 'ObjectDB instances leaked|resources still in use|SCRIPT ERROR:') { throw "Godot lifecycle error in $($log.Name)" }
}
Write-Host "[fusion-weapons] PASS. Local captures, recording and reports: $Out" -ForegroundColor Green
exit 0
