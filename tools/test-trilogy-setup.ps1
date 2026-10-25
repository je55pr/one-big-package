#Requires -Version 5.1
<#
.SYNOPSIS
  Exercise ordinary trilogy startup gates in the real headless host using isolated source configs.
#>
param([string]$Rac1Iso = $env:OBP_RAC1_ISO, [string]$GcIso = $env:OBP_GC_ISO, [string]$UyaIso = $env:OBP_UYA_ISO, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-trilogy-smoke.ps1') -Sources @($Rac1Iso, $GcIso, $UyaIso) -SkipBuild:$SkipBuild
$evidence = Join-Path $root ('captures/trilogy-setup/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$entries = @(
    @{ Game = 'Rac1'; BuildId = 'rac1-ntscu-original'; Path = $Rac1Iso },
    @{ Game = 'Rac2'; BuildId = 'rac2-ntscu-v1.01'; Path = $GcIso },
    @{ Game = 'Rac3'; BuildId = 'rac3-ntscu-original'; Path = $UyaIso }
)
$cases = @(
    @{ Name = 'zero-lab'; Count = 0; Destination = 'obp:fusion-lab'; Code = 2 },
    @{ Name = 'one-native'; Count = 1; Destination = 'rac1:LEVEL0'; Code = 2 },
    @{ Name = 'two-lab'; Count = 2; Destination = 'obp:fusion-lab'; Code = 2 },
    @{ Name = 'skip-title'; Count = 2; Code = 2 },
    @{ Name = 'three-lab'; Count = 3; Destination = 'obp:fusion-lab'; Code = 0 },
    @{ Name = 'missing-file'; Count = 3; Missing = $true; Destination = 'obp:fusion-lab'; Code = 2 },
    @{ Name = 'recovered-needs-repair'; Count = 3; Recovered = $true; Destination = 'obp:fusion-lab'; Code = 2 },
    @{ Name = 'explicit-single-game'; Count = 1; Destination = 'rac1:LEVEL0'; Diagnostic = $true; Code = 0 },
    @{ Name = 'live-source-loss'; Count = 3; Destination = 'obp:fusion-lab'; SourceLoss = $true; Code = 2 }
)
foreach ($case in $cases) {
    $config = Join-Path $evidence ($case.Name + '-sources.json')
    $selected = @($entries | Select-Object -First $case.Count | ForEach-Object { $_.Clone() })
    if ($case.Missing) { $selected[1].Path = Join-Path $evidence 'missing-disc.iso' }
    $json = [ordered]@{ Version = 1; Sources = $selected } | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($config, $json)
    if ($case.Recovered) { [IO.File]::WriteAllText($config + '.bak', $json); [IO.File]::WriteAllText($config, '{truncated') }
    $primary = [IO.File]::ReadAllText($config)
    $testProfilePath = Join-Path $evidence ($case.Name + '-profile.json')
    if (-not $case.SourceLoss) { [IO.File]::WriteAllText($testProfilePath, '{retained-invalid-profile') }
    $log = Join-Path $evidence ($case.Name + '.log')
    $errorLog = Join-Path $evidence ($case.Name + '.err.log')
    $arguments = @('--headless', '--path', (Join-Path $root 'game'), '--max-fps', '120', '--quit-after', '900',
        '--', '--source-config', $config, '--profile', $testProfilePath, '--skip-title')
    if ($case.Destination) { $arguments += @('--destination', $case.Destination) }
    if ($case.Diagnostic) { $arguments += '--diagnostic-sources' }
    if ($case.SourceLoss) { $arguments += '--source-setup-check' }
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
    $expected = if ($case.SourceLoss) { '[source-setup-check] PASS' } elseif ($case.Code -eq 2) { 'reason=trilogy-setup-required exitCode=2' } else { '[destinations] world ready' }
    if ($code -ne $case.Code -or -not (Select-String -LiteralPath $log -SimpleMatch $expected -Quiet) -or
        [IO.File]::ReadAllText($config) -ne $primary -or (-not $case.SourceLoss -and [IO.File]::ReadAllText($testProfilePath) -ne '{retained-invalid-profile')) {
        throw "Trilogy setup case $($case.Name) failed (exit $code). Inspect $evidence."
    }
    Write-Host "Trilogy setup $($case.Name) passed; config/profile retained"
}
Write-Host "Headless trilogy setup checks passed. Evidence: $evidence"
