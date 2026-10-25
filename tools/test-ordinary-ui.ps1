#Requires -Version 5.1
<#
.SYNOPSIS
  Verify ready navigation, resume, save recovery and genuine setup repair without desktop input.
#>
param([string]$Rac1Iso = $env:OBP_RAC1_ISO, [string]$GcIso = $env:OBP_GC_ISO, [string]$UyaIso = $env:OBP_UYA_ISO, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
foreach ($source in @($Rac1Iso, $GcIso, $UyaIso)) {
    if (-not $source -or -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Authorised trilogy sources are required.' }
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OBP.Game.csproj') -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Godot build failed.' }
}
$evidence = Join-Path $root ('captures/ordinary-ui/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$config = Join-Path $evidence 'sources.json'
$configText = [ordered]@{ Version = 1; Sources = @(
    @{ Game = 'Rac1'; BuildId = 'rac1-ntscu-original'; Path = $Rac1Iso },
    @{ Game = 'Rac2'; BuildId = 'rac2-ntscu-v1.01'; Path = $GcIso },
    @{ Game = 'Rac3'; BuildId = 'rac3-ntscu-original'; Path = $UyaIso }
) } | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText($config, $configText)
$savedGamePath = Join-Path $evidence 'progress.json'
$rejectedSavePath = Join-Path $evidence 'invalid-progress.json'
[IO.File]::WriteAllText($rejectedSavePath, '{retained-invalid-save')
foreach ($case in @('fresh', 'resume', 'invalid-save')) {
    $testSavePath = if ($case -eq 'invalid-save') { $rejectedSavePath } else { $savedGamePath }
    $log = Join-Path $evidence ($case + '.log')
    $errorLog = Join-Path $evidence ($case + '.err.log')
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList @('--headless', '--path', (Join-Path $root 'game'),
        '--max-fps', '120', '--quit-after', '5400', '--', '--ordinary-ui-smoke', $case, '--source-config', $config, '--profile', $testSavePath) `
        -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
    if ($code -ne 2 -or -not (Select-String -LiteralPath $log -SimpleMatch '[ordinary-ui-smoke] PASS' -Quiet)) { throw "Ordinary UI case $case failed (exit $code). Inspect $evidence." }
    if ([IO.File]::ReadAllText($config) -ne $configText -or [IO.File]::ReadAllText($rejectedSavePath) -ne '{retained-invalid-save') { throw 'Navigation/repair rewrote an input document.' }
    Write-Host "Ordinary UI $case passed; repair exit 2 and input documents retained"
}
Write-Host "Headless ordinary UI checks passed. Evidence: $evidence"
