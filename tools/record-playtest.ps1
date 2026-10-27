#Requires -Version 5.1
<#
.SYNOPSIS
  Start a source-qualified shared-player recording from a registered world entry.
.DESCRIPTION
  Play normally. F8 marks a bug; F9 saves the run and stops recording.
  With a controller, pause with Start, then Square marks and Select saves.
  Starting campaign progress is copied when supplied. The resulting run contains
  logical controls and state observations, with no ISO/assets or local source path.
#>
param(
    [ValidatePattern('^(rac[123]:(LEVEL|TABLE)[0-9]+|obp:fusion-lab)$')][string]$Destination = 'rac1:LEVEL1',
    [string]$CampaignSave,
    [string]$Profile,
    [string]$Out,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
    [Alias('UyaIso')][string]$Rac3Iso = $env:OBP_UYA_ISO,
    [switch]$FusionTest,
    [switch]$DefaultWeaponsSmoke,
    [int]$Seed = 1440,
    [switch]$SkipBuild,
    [switch]$Smoke,
    [switch]$ReconciliationSmoke,
    [switch]$Headless,
    [switch]$Offscreen
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($FusionTest -and ($Destination -ne 'rac1:LEVEL0' -or $CampaignSave)) { throw 'Fusion recording requires fresh rac1:LEVEL0 without a campaign save.' }
. (Join-Path $PSScriptRoot 'obp-window.ps1')
if ($Headless -and -not ($Smoke -or $ReconciliationSmoke)) { throw 'Headless recording needs an input smoke driver; human recordings use a visible window.' }
if ($Offscreen -and -not $Smoke) { throw 'Offscreen recording is only for the input smoke driver.' }
if (-not $Out) {
    $Out = Join-Path $root ('captures/repros/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8) + '.run.jsonl.gz')
}
$Out = [IO.Path]::GetFullPath($Out)
if ((Test-Path -LiteralPath $Out) -or (Test-Path -LiteralPath ($Out + '.partial'))) { throw 'Choose a new recording filename.' }
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Out)) -Force | Out-Null
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
if ($Headless) { $engineArgs += '--headless' }
elseif ($Offscreen) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
else { $engineArgs += @(Get-ObpWindowArguments); $engineArgs += @('--max-fps', '60') }
$engineArgs += @('--log-file', ($Out + '.log'), '--', '--destination', $Destination, '--record-playtest', $Out, '--fixed-seed', $Seed)
if ($Rac1Iso) { $engineArgs += @('--rac1-iso', $Rac1Iso) }
if ($Rac2Iso) { $engineArgs += @('--rac2-iso', $Rac2Iso) }
if ($Rac3Iso) { $engineArgs += @('--rac3-iso', $Rac3Iso) }
if ($FusionTest) { $engineArgs += '--fusion-test' }
if ($DefaultWeaponsSmoke) { $engineArgs += '--default-weapons-smoke' }
if ($Smoke) { $engineArgs += '--playtest-smoke' }
if ($ReconciliationSmoke) { $engineArgs += '--reconciliation-smoke' }
$smokeProfileDirectory = $null
try {
    if ($Profile) {
        $recordProfile = [IO.Path]::GetFullPath($Profile)
        if ($Smoke -or $ReconciliationSmoke) {
            $smokeProfileDirectory = Join-Path ([IO.Path]::GetTempPath()) ('obp-recording-profile-' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $smokeProfileDirectory | Out-Null
            $isolatedProfile = Join-Path $smokeProfileDirectory 'profile.json'
            foreach ($suffix in @('', '.bak')) {
                if (Test-Path -LiteralPath ($recordProfile + $suffix) -PathType Leaf) {
                    Copy-Item -LiteralPath ($recordProfile + $suffix) -Destination ($isolatedProfile + $suffix)
                }
            }
            $recordProfile = $isolatedProfile
        }
        $engineArgs += @('--profile', $recordProfile)
    }
    elseif ($Smoke -or $ReconciliationSmoke) { $engineArgs += '--no-profile' }
    if ($CampaignSave) {
        if (-not (Test-Path -LiteralPath $CampaignSave -PathType Leaf)) { throw 'Starting campaign save not found.' }
        $isolatedCampaign = $Out + '.campaign.json'
        if (Test-Path -LiteralPath $isolatedCampaign) { throw 'Starting campaign copy already exists.' }
        Copy-Item -LiteralPath $CampaignSave -Destination $isolatedCampaign
        $engineArgs += @('--rac1-campaign-save', $isolatedCampaign)
    }
    Write-Host "Recording $Destination. F8 marks; F9 saves. Paused controller: Square marks, Select saves." -ForegroundColor Cyan
    Write-Host "Run: $Out"
    & $godot @engineArgs
    $recordExit = $LASTEXITCODE
}
finally {
    if ($smokeProfileDirectory) {
        foreach ($suffix in @('', '.bak')) {
            $ownedProfile = Join-Path $smokeProfileDirectory ('profile.json' + $suffix)
            if (Test-Path -LiteralPath $ownedProfile -PathType Leaf) { Remove-Item -LiteralPath $ownedProfile -Force }
        }
        # Non-recursive: unexpected files cannot cause deletion outside this owned copy.
        Remove-Item -LiteralPath $smokeProfileDirectory
    }
}
if ($recordExit -ne 0) { exit $recordExit }
if (-not (Test-Path -LiteralPath $Out -PathType Leaf)) { throw 'No completed recording was saved; inspect the log/partial file.' }
Write-Host "Saved run: $Out" -ForegroundColor Green
