#Requires -Version 5.1
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$Out = 'captures/player-capabilities-smoke'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$root = Split-Path -Parent $PSScriptRoot
$godot = & (Join-Path $PSScriptRoot 'prepare-trilogy-smoke.ps1') -Sources @($Rac1Iso, $GcIso, $UyaIso) -SkipBuild:$SkipBuild
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$baseline = $null
foreach ($fps in @(30, 120)) {
    $engineArgs = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', "$fps")
    if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
    else { $engineArgs += '--headless' }
    $report = Join-Path $Out "trace-$fps.json"
    $engineArgs += @('--', '--destination', 'rac1:LEVEL0', '--player-capabilities-smoke', '--player-capabilities-out', $report, '--fixed-seed', '1440',
        '--rac1-iso', $Rac1Iso, '--gc-iso', $GcIso, '--uya-iso', $UyaIso)
    $stdout = Join-Path $Out "stdout-$fps.log"
    $stderr = Join-Path $Out "stderr-$fps.log"
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $engineArgs -WorkingDirectory $root -StandardOutputPath $stdout -StandardErrorPath $stderr
    Get-Content -LiteralPath $stdout -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    Get-Content -LiteralPath $stderr -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if ($code -ne 0 -or -not (Select-String -LiteralPath $stdout -SimpleMatch '[player-capabilities-smoke] PASS:')) {
        throw "Player capabilities smoke failed at $fps rendering FPS ($code). Inspect $Out"
    }
    if (Select-String -LiteralPath $stderr -Pattern 'ObjectDB instances leaked|resources still in use|SCRIPT ERROR:') {
        throw "Godot lifecycle failure. Inspect $stderr"
    }
    $trace = Get-Content -LiteralPath $report -Raw
    if ($null -ne $baseline -and $trace -cne $baseline) { throw 'Native player trace differs between 30 and 120 rendering FPS.' }
    $baseline = $trace
}
Write-Host '[player-capabilities] PASS: identical native traces at 30 and 120 rendering FPS' -ForegroundColor Green
