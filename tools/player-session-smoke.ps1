#Requires -Version 5.1
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$Out = 'captures/player-session-smoke'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')
$root = Split-Path -Parent $PSScriptRoot
foreach ($source in @($Rac1Iso, $GcIso, $UyaIso)) {
    if (-not $source -or -not (Test-Path -LiteralPath $source)) { throw 'All three authorised original sources are required.' }
}
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $Out -Force | Out-Null
if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) { & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null }
$engineArgs = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', '60')
if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
else { $engineArgs += '--headless' }
$engineArgs += @('--', '--destination', 'rac1:LEVEL0', '--player-session-smoke', '--fixed-seed', '1440',
    '--rac1-iso', $Rac1Iso, '--gc-iso', $GcIso, '--uya-iso', $UyaIso)
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
