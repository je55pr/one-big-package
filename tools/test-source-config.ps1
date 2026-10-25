#Requires -Version 5.1
<#
.SYNOPSIS
  Verify remembered-source recovery in the real headless host with isolated config files.
#>
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso, $GcIso, $UyaIso) -SkipBuild:$SkipBuild
$evidence = Join-Path $root ('captures/source-config/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$config = Join-Path $evidence 'sources.json'
$valid = [ordered]@{ Version = 1; Sources = @(
    @{ Game = 'Rac1'; BuildId = 'rac1-ntscu-original'; Path = $Rac1Iso },
    @{ Game = 'Rac2'; BuildId = 'rac2-ntscu-v1.01'; Path = $GcIso },
    @{ Game = 'Rac3'; BuildId = 'rac3-ntscu-original'; Path = $UyaIso }
) } | ConvertTo-Json -Depth 6
foreach ($case in @(
    @{ Name = 'backup'; Backup = $valid; Expected = '3/3 sources attached'; Warning = 'Recovered the last valid backup' },
    @{ Name = 'rejected'; Backup = 'broken backup'; Expected = '0/3 sources attached'; Warning = 'Document and backup could not be loaded' }
)) {
    [IO.File]::WriteAllText($config, '{truncated')
    [IO.File]::WriteAllText($config + '.bak', $case.Backup)
    $log = Join-Path $evidence ($case.Name + '.log')
    $errorLog = Join-Path $evidence ($case.Name + '.err.log')
    $arguments = @('--headless', '--path', (Join-Path $root 'game'), '--fixed-fps', '60', '--max-fps', '120', '--quit-after', '600',
        '--', '--test-scene', 'picker', '--source-config', $config, '--no-profile', '--automation')
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
    if ($code -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch $case.Expected -Quiet) -or
        -not (Select-String -LiteralPath $errorLog -SimpleMatch $case.Warning -Quiet) -or
        [IO.File]::ReadAllText($config) -ne '{truncated' -or [IO.File]::ReadAllText($config + '.bak') -ne $case.Backup) {
        throw "Source-config case $($case.Name) failed. Inspect $evidence."
    }
    Write-Host "Source-config $($case.Name) passed; original files retained"
}
Write-Host "Headless source recovery checks passed. Evidence: $evidence"
