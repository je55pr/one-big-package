#Requires -Version 5.1
<# Launch the explicitly OBP-authored eight-Tyhrranoid diagnostic.
   Uses original game sources without changing user profiles, regular Fusion Lab or retail files.
   Class-5821 movement, native state scheduling, death and rewards are NOT recovered.
#>
param([string]$SourceConfig)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $root 'captures/tyhrranoid-test'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

if ([string]::IsNullOrWhiteSpace($SourceConfig)) {
    $SourceConfig = Join-Path $evidence 'sources.json'
    if (-not (Test-Path -LiteralPath $SourceConfig)) {
        $attached = @(
            @{ Game = 'Rac1'; BuildId = 'rac1-ntscu-original'; Path = $env:OBP_RAC1_ISO },
            @{ Game = 'Rac2'; BuildId = 'rac2-ntscu-v1.01'; Path = $env:OBP_GC_ISO },
            @{ Game = 'Rac3'; BuildId = 'rac3-ntscu-original'; Path = $env:OBP_UYA_ISO }
        )
        if (@($attached | Where-Object { -not $_.Path -or -not (Test-Path -LiteralPath $_.Path -PathType Leaf) }).Count -ne 0) {
            throw 'Attach the three original game sources first, or specify an existing -SourceConfig.'
        }
        [IO.File]::WriteAllText($SourceConfig, ([ordered]@{ Version = 1; Sources = $attached } |
            ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    }
}
if (-not (Test-Path -LiteralPath $SourceConfig -PathType Leaf)) {
    throw 'The source configuration file is missing.'
}

$toolchain = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($toolchain.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    throw 'The pinned Godot 4.7.2 build is missing. Run tools/bootstrap-godot.ps1 -Platform win64.'
}
if (-not (Test-Path -LiteralPath (Join-Path $root 'game/.godot/mono/temp/bin/Debug'))) {
    throw 'The Godot Debug assembly is missing. Build the game before launching.'
}

$run = Get-Date -Format 'yyyyMMdd-HHmmss'
$stdout = Join-Path $evidence "$run.log"
$stderr = Join-Path $evidence "$run.err.log"
$arguments = @('--path', (Join-Path $root 'game'), '--', '--source-config', $SourceConfig,
    '--no-profile', '--skip-title', '--destination', 'obp:fusion-lab', '--tyhrranoid-test')
$process = Start-Process -FilePath $godot -ArgumentList $arguments -WorkingDirectory $root -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Write-Host "[tyhrranoid-test] Visible Godot started (pid $($process.Id))."
Write-Host '[tyhrranoid-test] Eight retail-model class-5821 receivers plus OBP-only action-12 visual preview.'
Write-Host "[tyhrranoid-test] Output: $stdout"
