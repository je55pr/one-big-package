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
    & (Join-Path $PSScriptRoot 'build.ps1') -Game -WarningsAsErrors
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
    @{ Name = 'rac2-aliases'; Options = @('--rac2-iso', 'a.iso', '--gc-iso', 'b.iso'); Expected = '--rac2-iso/--gc-iso specify conflicting source paths' },
    @{ Name = 'rac3-aliases'; Options = @('--rac3-iso', 'a.iso', '--uya-iso', 'b.iso'); Expected = '--rac3-iso/--uya-iso specify conflicting source paths' },
    @{ Name = 'modes'; Options = @('--record-playtest', 'a', '--replay-playtest', 'b'); Expected = '--record-playtest cannot be combined' },
    @{ Name = 'engine-after-boundary'; Options = @('--headless'); Expected = 'Unknown OBP argument: --headless' },
    @{ Name = 'retired-viewer'; Options = @('--compose'); Expected = 'Unknown OBP argument: --compose' },
    @{ Name = 'retired-viewer-document'; Options = @('--composition', 'old.json'); Expected = 'Unknown OBP argument: --composition' },
    @{ Name = 'source-missing'; Options = @('--rac1-iso', (Join-Path $evidence 'missing-disc.iso'), '--source-config', (Join-Path $evidence 'rejected-sources.json'), '--no-profile'); Prefix = '[sources] '; Expected = 'Could not attach Ratchet & Clank'; Reason = 'source-argument-invalid' },
    @{ Name = 'source-missing-visibility'; Options = @('--rac1-iso', (Join-Path $evidence 'missing-disc.iso'), '--source-config', (Join-Path $evidence 'rejected-sources.json'), '--no-profile', '--rac1-startup-visibility-smoke'); Prefix = '[sources] '; Expected = 'Could not attach Ratchet & Clank'; Reason = 'source-argument-invalid' }
)
foreach ($case in $cases) {
    $log = Join-Path $evidence ($case.Name + '.log')
    $errorLog = Join-Path $evidence ($case.Name + '.err.log')
    $arguments = @('--headless', '--path', (Join-Path $root 'game'), '--quit-after', '120')
    if ($case.Scene) { $arguments += $case.Scene }
    $arguments += @('--') + $case.Options
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
    $prefix = if ($case.Prefix) { $case.Prefix } else { '[cli] ' }
    $reason = if ($case.Reason) { $case.Reason } else { 'cli-invalid' }
    if ($code -ne 2 -or -not (Select-String -LiteralPath $errorLog -SimpleMatch ($prefix + $case.Expected) -Quiet) -or
        -not (Select-String -LiteralPath $log -SimpleMatch ("reason=$reason exitCode=2") -Quiet)) {
        throw "CLI case $($case.Name) failed (exit $code). Inspect $evidence."
    }
    if ($reason -eq 'source-argument-invalid' -and (
        (Select-String -LiteralPath $log -Pattern '\[sources\] source manager shown|\[destinations\] world ready' -Quiet) -or
        (Select-String -LiteralPath $errorLog -Pattern 'not bound|SCRIPT ERROR:|InvalidOperationException' -Quiet))) {
        throw "Rejected source startup continued through its deferred bootstrap: $($case.Name). Inspect $evidence."
    }
    Write-Host "CLI rejection $($case.Name) passed"
}
$log = Join-Path $evidence 'valid-bootstrap.log'
$errorLog = Join-Path $evidence 'valid-bootstrap.err.log'
$arguments = @('--headless', '--path', (Join-Path $root 'game'), '--fixed-fps', '60', '--quit-after', '120',
    '--', '--automation', '--test-scene', 'picker', '--source-config', (Join-Path $evidence 'empty-sources.json'), '--no-profile', '--fixed-seed', '-1')
$code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -StandardOutputPath $log -StandardErrorPath $errorLog
if ($code -ne 0 -or -not (Select-String -LiteralPath $log -SimpleMatch '[OBPGame] ready' -Quiet) -or
    (Select-String -LiteralPath $errorLog -SimpleMatch '[cli]' -Quiet)) {
    throw "Valid CLI bootstrap failed (exit $code). Inspect $evidence."
}
Write-Host "CLI lifecycle checks passed. Evidence: $evidence"
