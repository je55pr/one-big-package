#Requires -Version 5.1
<#
.SYNOPSIS
  Verify the Lab, screenshot, crate and stress CLI contracts with isolated local artifacts.
.DESCRIPTION
  The default checks are source-free. -Retail also uses all three authorised
  source environment variables. GPU captures use unfocusable offscreen windows;
  navigation/cancellation and ordinary Lab checks are headless.
#>
[CmdletBinding()]
param(
    [switch]$Retail,
    [switch]$SkipBuild,
    [string]$Out,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$Rac2Iso = $env:OBP_GC_ISO,
    [string]$Rac3Iso = $env:OBP_UYA_ISO
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
if (-not $Out) { $Out = Join-Path $repoRoot ('captures/diagnostic-cli/' + [guid]::NewGuid().ToString('N')) }
$Out = [IO.Path]::GetFullPath($Out)
if (Test-Path -LiteralPath $Out) { throw 'Choose a new evidence directory to keep profiles/source configuration isolated.' }
New-Item -ItemType Directory -Path $Out | Out-Null
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$engine = Join-Path $repoRoot ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game -WarningsAsErrors
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
}
$sources = @()
if ($Retail) {
    foreach ($source in @($Rac1Iso, $Rac2Iso, $Rac3Iso)) {
        if (-not $source -or -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Set all three authorised source environment variables for -Retail.' }
    }
    $sources = @('--rac1-iso', $Rac1Iso, '--rac2-iso', $Rac2Iso, '--rac3-iso', $Rac3Iso)
}
$results = New-Object System.Collections.Generic.List[object]
function Invoke-CliCheck {
    param([string]$Name, [string[]]$Options, [string]$Expected, [int]$ExpectedExit = 0, [switch]$Headless, [string[]]$Entry = @())
    $engineArgs = @('--path', (Join-Path $repoRoot 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', '60')
    if ($Headless) { $engineArgs += '--headless' }
    else { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '1280x720') }
    $engineArgs += $Entry + @('--', '--no-profile', '--fixed-seed', '1440', '--source-config', (Join-Path $Out ($Name + '-sources.json'))) + $Options
    $log = Join-Path $Out ($Name + '.log'); $errors = Join-Path $Out ($Name + '.err.log')
    $code = Invoke-ObpNativeProcessCapture -FilePath $engine -ArgumentList $engineArgs -WorkingDirectory $repoRoot -StandardOutputPath $log -StandardErrorPath $errors
    $passed = $code -eq $ExpectedExit -and (Select-String -LiteralPath $log -SimpleMatch $Expected -Quiet)
    if (Select-String -LiteralPath $errors -Pattern 'SCRIPT ERROR:|ObjectDB instances leaked|resources still in use' -Quiet) { $passed = $false }
    $results.Add([pscustomobject]@{ Name = $Name; Passed = $passed; ExitCode = $code; Log = ($Name + '.log') })
    if (-not $passed) { throw "Diagnostic CLI check $Name failed. Inspect its local logs." }
    Write-Host "$Name passed"
}
try {
    Invoke-CliCheck 'lab' @('--test-scene', 'fusion-lab', '--diagnostic-player', '--capture-frame', '1800', '--capture-out', (Join-Path $Out 'lab.png')) '[fusion-smoke] PASS:'
    Invoke-CliCheck 'postdraw' @('--shots-out', (Join-Path $Out 'phase')) '[cli-tool-engine-smoke] PASS:' -Entry @('res://diagnostics/CliToolEngineSmoke.tscn')
    Invoke-CliCheck 'bad-list' @('--shots', (Join-Path $Out 'missing.json')) 'reason=shots-list-read-failed exitCode=2' -ExpectedExit 2 -Headless
    $single = Join-Path $Out 'single.json'
    [IO.File]::WriteAllText($single, '{"shots":[{"name":"top","camera":"topDown","settleFrames":1}]}')
    Invoke-CliCheck 'missing-source' @('--shots', $single, '--shots-world', 'oozla') 'reason=shots-missing-gc-source exitCode=2' -ExpectedExit 2 -Headless
    if ($Retail) {
        foreach ($variant in @('focus', 'strike')) {
            $crateArgs = $sources + @('--test-scene', 'player', '--diagnostic-player', '--destination', 'rac2:LEVEL1', '--crate-focus', '--capture-frame', '120', '--capture-out', (Join-Path $Out ("crate-$variant.png")))
            if ($variant -eq 'strike') { $crateArgs += '--crate-auto-strike' }
            Invoke-CliCheck ("crate-$variant") $crateArgs 'reason=capture-complete exitCode=0'
            $crate = Get-Content -LiteralPath (Join-Path $Out ("crate-$variant.json")) -Raw | ConvertFrom-Json
            if ($null -eq $crate.crateDebug -or $crate.crateDebug.NativeInstanceIndex -ne 31 -or $crate.crateDebug.EventFlags -ne '0x00010000' -or $crate.crateDebug.EventScalar -ne 2) { throw 'Crate capture lost its original source-qualified target/damage tuple.' }
            if ($variant -eq 'strike' -and (-not $crate.crateDebug.Broken -or $crate.crateDebug.Route -ne 'Deactivate')) { throw 'Crate auto-strike did not retain its native consequence.' }
        }
        foreach ($game in @('rac1', 'rac2', 'rac3')) {
            Invoke-CliCheck ("shots-$game") ($sources + @('--shots', (Join-Path $repoRoot "tools/shots/$game.json"), '--shots-out', (Join-Path $Out 'shots'))) 'reason=shots-complete exitCode=0'
        }
        Invoke-CliCheck 'stress' ($sources + @('--stress-switch', 'rac2:LEVEL1,__bad-import,unknown,rac1:LEVEL0,rac3:TABLE1')) '[stress] PASS rejected bad import, retained world/player/campaign' -Headless
        Invoke-CliCheck 'stress-canceled' ($sources + @('--stress-switch', 'rac2:LEVEL1,rac3:TABLE1')) 'reason=desktop-window-close exitCode=1' -ExpectedExit 1 -Headless -Entry @('--script', 'res://tests/stress_switch_cancellation_smoke.gd')
        $canceledLog = Join-Path $Out 'stress-canceled.log'
        if (-not (Select-String -LiteralPath $canceledLog -SimpleMatch '[stress-cancellation-smoke] requesting close' -Quiet) -or
            (Select-String -LiteralPath $canceledLog -Pattern 'stress-switch-complete|TABLE1' -Quiet)) { throw 'Canceled stress operation completed or entered its later world.' }
        [IO.File]::WriteAllText((Join-Path $Out 'blocked'), 'file')
        Invoke-CliCheck 'bad-output' ($sources + @('--shots', $single, '--shots-world', 'rac2:LEVEL1', '--shots-out', (Join-Path $Out 'blocked/child'))) 'reason=shots-failed exitCode=2' -ExpectedExit 2
        Invoke-CliCheck 'ordinary-lab' ($sources + @('--destination', 'obp:fusion-lab')) '[lab-presentation-check] PASS:' -Headless -Entry @('--script', 'res://tests/lab_presentation_check.gd')
    }
    $status = 'passed'
}
catch { $status = 'failed'; throw }
finally {
    [ordered]@{ Version = 1; Result = $status; Retail = [bool]$Retail; OffscreenAndUnfocusable = $true; Checks = @($results.ToArray()) } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Out 'summary.json') -Encoding UTF8
    Write-Host "Diagnostic CLI evidence: $Out"
}
