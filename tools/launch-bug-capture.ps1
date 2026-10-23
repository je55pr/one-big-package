#Requires -Version 5.1
<#
.SYNOPSIS
  Double-clickable, opt-in R&C1 bug capture. The game owns reporting and its local inbox.
.DESCRIPTION
  Starts at ordinary level entry with a copy of the current campaign. F8 reports;
  with a controller, pause with Start and press Square. No terminal interaction.
#>
param(
    [switch]$SkipBuild,
    [switch]$Smoke,
    [ValidatePattern('^rac1:LEVEL([0-9]|1[0-8])$')][string]$Destination,
    [string]$CampaignSave,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'obp-window.ps1')
$captureRoot = Join-Path $root 'captures/bug-reports'
if ($Smoke) { $captureRoot = Join-Path $captureRoot ('launcher-smoke-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
$launchLog = Join-Path $captureRoot 'launcher.log'

try {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
    $console = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
    if (-not (Test-Path -LiteralPath $console -PathType Leaf)) {
        & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 *> $launchLog
        if ($LASTEXITCODE -ne 0) { throw 'Could not prepare Godot.' }
    }
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo *> $launchLog
        if ($LASTEXITCODE -ne 0) { throw 'The game build failed.' }
    }
    & $console --headless --editor --import --path (Join-Path $root 'game') --quit *>> $launchLog
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare game resources.' }
    $game = $console -replace '_console\.exe$', '.exe'
    if (-not (Test-Path -LiteralPath $game -PathType Leaf)) { $game = $console }
    $gameArguments = @('--path', (Join-Path $root 'game'), '--fixed-fps', '60')
    if ($Smoke) { $gameArguments += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x540', '--rendering-method', 'gl_compatibility', '--log-file', (Join-Path $captureRoot 'smoke.log')) }
    else { $gameArguments += @(Get-ObpWindowArguments) }
    $gameArguments += @('--', '--bug-capture')
    if ($Smoke) { $gameArguments += @('--bug-report-smoke', '--bug-report-inbox', $captureRoot) }
    if ($Destination) { $gameArguments += @('--destination', $Destination) }
    if ($CampaignSave) { $gameArguments += @('--rac1-campaign-save', [IO.Path]::GetFullPath($CampaignSave)) }
    if ($Rac1Iso) { $gameArguments += @('--rac1-iso', $Rac1Iso) }
    # Start-Process joins its array as a native command line on Windows. Quote
    # each argument using the CRT rules, including spaces and trailing slashes.
    $quoted = $gameArguments | ForEach-Object { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
    $process = Start-Process -FilePath $game -ArgumentList $quoted -WorkingDirectory $root -PassThru
    if ($Smoke) { [pscustomobject]@{ ProcessId = $process.Id; Inbox = $captureRoot } }
}
catch {
    if ($Smoke) { throw }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("$($_.Exception.Message)`nDetails: $launchLog", 'OBP Bug Capture') | Out-Null
    exit 1
}
