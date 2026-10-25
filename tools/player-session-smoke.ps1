#Requires -Version 5.1
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
    [Alias('UyaIso')][string]$Rac3Iso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$Out = 'captures/player-session-smoke'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$root = Split-Path -Parent $PSScriptRoot
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso, $Rac2Iso, $Rac3Iso) -SkipBuild:$SkipBuild
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$engineArgs = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', '60')
if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
else { $engineArgs += '--headless' }
$engineArgs += @('--', '--destination', 'rac1:LEVEL0', '--player-session-smoke', '--fixed-seed', '1440',
    '--rac1-iso', $Rac1Iso, '--rac2-iso', $Rac2Iso, '--rac3-iso', $Rac3Iso)
$stdout = Join-Path $Out 'stdout.log'
$stderr = Join-Path $Out 'stderr.log'
$code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $engineArgs -WorkingDirectory $root -StandardOutputPath $stdout -StandardErrorPath $stderr
Get-Content -LiteralPath $stdout -Encoding UTF8 | ForEach-Object { Write-Host $_ }
Get-Content -LiteralPath $stderr -Encoding UTF8 | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or -not (Select-String -LiteralPath $stdout -SimpleMatch '[player-session-smoke] PASS:')) {
    throw "Player session lifecycle smoke failed ($code). Inspect $Out"
}
if (Select-String -LiteralPath $stderr -Pattern 'ObjectDB instances leaked|resources still in use|SCRIPT ERROR:') {
    throw "Godot lifecycle failure. Inspect $stderr"
}
Write-Host '[player-session] PASS' -ForegroundColor Green
