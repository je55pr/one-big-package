#Requires -Version 5.1
<#
.SYNOPSIS
  Replay a selected local bug report to its exact marker using the existing playtest reader.
.DESCRIPTION
  Players use the in-game inbox; this wrapper is a convenience for developer triage.
#>
param(
    [Parameter(Mandatory=$true)][string]$Session,
    [Parameter(Mandatory=$true)][guid]$ReportId,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [switch]$AllowBuildChange,
    [switch]$Rendered,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $Session -PathType Leaf)) { throw 'Choose the saved session.json manifest.' }
$Session = [IO.Path]::GetFullPath($Session)
$result = Join-Path (Split-Path -Parent $Session) ('replay-' + [guid]::NewGuid().ToString('N') + '.json')
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$engineArgs = @('--path', (Join-Path $root 'game'), '--fixed-fps', '60')
if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--rendering-method', 'gl_compatibility') }
else { $engineArgs += '--headless' }
$gameArgs = @('--', '--replay-bug-report', $Session, '--bug-report-id', $ReportId.ToString(), '--playtest-report', $result)
if ($Rac1Iso) { $gameArgs += @('--rac1-iso', $Rac1Iso) }
if ($AllowBuildChange) { $gameArgs += '--playtest-allow-build-change' }
& $godot @engineArgs @gameArgs
$code = $LASTEXITCODE
Write-Host "Replay result: $result"
if ($code -eq 0) {
    if (-not (Test-Path -LiteralPath $result -PathType Leaf)) { exit 2 }
    $data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($data.Result -ne 'matched' -or $data.Steps -ne ($data.ReportMarker.Step + 1)) { exit 3 }
}
exit $code
