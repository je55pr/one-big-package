#Requires -Version 5.1
<#
.SYNOPSIS
  Replay a recorded human test in a fresh process and compare state checkpoints.
.DESCRIPTION
  The file supplies the starting campaign, source identity, seed, camera options,
  viewport sizes, timed input and callback order. No observed world state is
  injected. Exit 0 means observed checkpoints matched, 3 means divergence, and
  2 means incompatible/invalid/incomplete input. Cross-build replay is explicit.
#>
param(
    [Parameter(Mandatory = $true)][string]$Run,
    [string]$Report,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$AllowBuildChange,
    [switch]$Rendered,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$root = Split-Path -Parent $PSScriptRoot
$Run = [IO.Path]::GetFullPath($Run)
if (-not (Test-Path -LiteralPath $Run -PathType Leaf)) { throw 'Recording not found.' }
if (-not $Report) { $Report = $Run + '.report.json' }
$Report = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Report)) -Force | Out-Null
if (Test-Path -LiteralPath $Report -PathType Leaf) { Remove-Item -LiteralPath $Report }
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $godot '--headless' '--editor' '--path' (Join-Path $root 'game') '--quit' | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$engineArgs = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', '60')
if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
else { $engineArgs += '--headless' }
$engineArgs += @('--log-file', ($Report + '.log'), '--', '--replay-playtest', $Run, '--playtest-report', $Report)
if ($Rac1Iso) { $engineArgs += @('--rac1-iso', $Rac1Iso) }
if ($GcIso) { $engineArgs += @('--gc-iso', $GcIso) }
if ($UyaIso) { $engineArgs += @('--uya-iso', $UyaIso) }
if ($AllowBuildChange) { $engineArgs += '--playtest-allow-build-change' }
$stdoutPath = $Report + '.stdout.log'
$stderrPath = $Report + '.stderr.log'
$replayExit = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $engineArgs -WorkingDirectory $root `
    -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
Get-Content -LiteralPath $stdoutPath -Encoding UTF8 | ForEach-Object { Write-Host $_ }
Get-Content -LiteralPath $stderrPath -Encoding UTF8 | ForEach-Object { Write-Host $_ }
Write-Host "Replay report: $Report"
if ($replayExit -eq 0) {
    if (-not (Test-Path -LiteralPath $Report -PathType Leaf)) { exit 2 }
    $result = (Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json).Result
    if ($result -eq 'diverged') { exit 3 }
    if ($result -ne 'matched') { exit 2 }
}
exit $replayExit
