#Requires -Version 5.1
<#
.SYNOPSIS
  Prove OBP CLI startup rejection and valid boundary handling without desktop input.
#>
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
}
$evidence = Join-Path $root ('captures/cli/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$cases = @(
    @{ Name = 'unknown'; Options = @('--unknown-obp-option'); Expected = 'Unknown OBP argument: --unknown-obp-option' },
    @{ Name = 'positional'; Options = @('unexpected'); Expected = 'Unknown OBP argument: unexpected' },
    @{ Name = 'missing'; Options = @('--rac2-iso'); Expected = '--rac2-iso requires a value' },
    @{ Name = 'flag-value'; Options = @('--capture-out', '--skip-title'); Expected = '--capture-out requires a value' },
    @{ Name = 'integer'; Options = @('--gc-level', 'invalid'); Expected = '--gc-level requires a 32-bit integer' },
    @{ Name = 'range'; Options = @('--load-movement-state', 'state.json', '--replay-forward', '3601'); Expected = '--replay-forward requires a value from 1 through 3600' },
    @{ Name = 'aliases'; Options = @('--rac3-iso', 'a.iso', '--uya-iso', 'b.iso'); Expected = '--rac3-iso/--uya-iso specify conflicting source paths' },
    @{ Name = 'modes'; Options = @('--record-playtest', 'a', '--replay-playtest', 'b'); Expected = '--record-playtest cannot be combined' },
    @{ Name = 'engine-after-boundary'; Options = @('--headless'); Expected = 'Unknown OBP argument: --headless' },
    @{ Name = 'composition-scene'; Scene = 'res://scenes/CompositionLab.tscn'; Options = @('--composition'); Expected = '--composition requires a value' }
)
foreach ($case in $cases) {
    $log = Join-Path $evidence ($case.Name + '.log')
    $errorLog = Join-Path $evidence ($case.Name + '.err.log')
    $arguments = @('--headless', '--path', (Join-Path $root 'game'), '--quit-after', '120')
    if ($case.Scene) { $arguments += $case.Scene }
    $arguments += @('--') + $case.Options
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
    if ($code -ne 2 -or -not (Select-String -LiteralPath $errorLog -SimpleMatch ('[cli] ' + $case.Expected) -Quiet) -or
        -not (Select-String -LiteralPath $log -SimpleMatch 'reason=cli-invalid exitCode=2' -Quiet)) {
        throw "CLI case $($case.Name) failed (exit $code). Inspect $evidence."
    }
    Write-Host "CLI rejection $($case.Name) passed"
}
$log = Join-Path $evidence 'valid-bootstrap.log'
$errorLog = Join-Path $evidence 'valid-bootstrap.err.log'
$arguments = @('--headless', '--path', (Join-Path $root 'game'), '--fixed-fps', '60', '--quit-after', '120',
    '--', '--automation', '--no-profile', '--fixed-seed', '-1')
$code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
if ($code -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch '[OBPGame] ready' -Quiet) -or
    (Select-String -LiteralPath $errorLog -SimpleMatch '[cli]' -Quiet)) {
    throw "Valid CLI bootstrap failed (exit $code). Inspect $evidence."
}
Write-Host "CLI lifecycle checks passed. Evidence: $evidence"
