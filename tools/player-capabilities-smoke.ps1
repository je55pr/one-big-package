#Requires -Version 5.1
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [Alias('GcIso')][string]$Rac2Iso = $env:OBP_GC_ISO,
    [Alias('UyaIso')][string]$Rac3Iso = $env:OBP_UYA_ISO,
    [switch]$Rendered,
    [switch]$SkipBuild,
    [string]$Out = 'captures/player-capabilities-smoke'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')
$root = Split-Path -Parent $PSScriptRoot
$godot = & (Join-Path $PSScriptRoot 'prepare-game-smoke.ps1') -Sources @($Rac1Iso, $Rac2Iso, $Rac3Iso) -SkipBuild:$SkipBuild
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Path $Out -Force | Out-Null
function Test-ObpPlayerTrialNumber($Value) {
    return ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) -and
        -not [double]::IsNaN([double]$Value) -and -not [double]::IsInfinity([double]$Value)
}
$baseline = $null
foreach ($fps in @(30, 120)) {
    $engineArgs = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--fixed-fps', "$fps")
    if ($Rendered) { $engineArgs += @('--windowed', '--position', '-20000,-20000', '--resolution', '960x720') }
    else { $engineArgs += '--headless' }
    $report = Join-Path $Out "trace-$fps.json"
    # These generated reports are overwritten by the scenario. An older run
    # must not satisfy the current process's result checks.
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $engineArgs += @('--', '--destination', 'rac1:LEVEL0', '--player-capabilities-smoke', '--player-capabilities-out', $report, '--fixed-seed', '1440',
        '--rac1-iso', $Rac1Iso, '--rac2-iso', $Rac2Iso, '--rac3-iso', $Rac3Iso)
    $stdout = Join-Path $Out "stdout-$fps.log"
    $stderr = Join-Path $Out "stderr-$fps.log"
    $code = Invoke-ObpNativeProcessCapture -FilePath $godot -ArgumentList $engineArgs -WorkingDirectory $root -StandardOutputPath $stdout -StandardErrorPath $stderr
    Get-Content -LiteralPath $stdout -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    Get-Content -LiteralPath $stderr -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    $log = [IO.File]::ReadAllText($stdout) + "`n" + [IO.File]::ReadAllText($stderr)
    if (-not (Test-ObpSmokeLog -ExitCode $code -Log $log -CompletionMarker '[player-capabilities-smoke] PASS:')) {
        throw "Player capabilities smoke failed at $fps rendering FPS ($code). Inspect $Out"
    }
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) { throw 'Native player report was not produced by this run.' }
    $trace = Get-Content -LiteralPath $report -Raw
    try { $document = $trace | ConvertFrom-Json }
    catch { throw "Native player report at $fps FPS is invalid JSON: $report. $($_.Exception.Message)" }
    $weapons = @($document.Baselines.PSObject.Properties)
    if ($document.Version -ne 1 -or $weapons.Count -ne 2 -or
        @(Compare-Object @('rac2/item:30', 'rac3/item:43') @($weapons.Name) -CaseSensitive).Count -ne 0) {
        throw 'Native player report is missing its two weapon baselines.'
    }
    foreach ($weapon in $weapons) {
        $samples = @($weapon.Value)
        if ($samples.Count -ne 40 -or $samples[-1].Tick -ne 240 -or $samples[-1].Clock -ne 4) {
            throw 'Native player report does not contain a complete four-second trial.'
        }
        for ($index = 0; $index -lt $samples.Count; $index++) {
            $sample = $samples[$index]; $tick = ($index + 1) * 6
            if (-not (Test-ObpPlayerTrialNumber $sample.Tick) -or $sample.Tick -ne $tick -or
                -not (Test-ObpPlayerTrialNumber $sample.Clock) -or [Math]::Abs($sample.Clock - $tick / 60.0) -gt 1e-9) {
                throw 'Native player report does not contain a complete four-second trial.'
            }
            foreach ($field in @('Tick', 'Sequence', 'Frame', 'Pose', 'Attachment', 'Facing', 'Position', 'Camera', 'Clock')) {
                if ($null -eq $sample.PSObject.Properties[$field]) { throw "Native player report has missing or invalid native sample fields: $field." }
            }
            if ($sample.Pose -isnot [string] -or $sample.Pose -notmatch '^[a-f0-9]{64}$' -or
                $sample.Attachment -isnot [string] -or [string]::IsNullOrWhiteSpace($sample.Attachment) -or
                -not (Test-ObpPlayerTrialNumber $sample.Facing) -or
                ($null -ne $sample.Sequence -and $sample.Sequence -isnot [string]) -or
                ($null -ne $sample.Frame -and (-not (Test-ObpPlayerTrialNumber $sample.Frame) -or [Math]::Truncate($sample.Frame) -ne $sample.Frame))) {
                throw 'Native player report has missing or invalid native sample fields.'
            }
            try { $null = $sample.Attachment | ConvertFrom-Json }
            catch { throw 'Native player report has missing or invalid native sample fields: attachment JSON.' }
            foreach ($vector in @($sample.Position, $sample.Camera)) {
                if (@($vector).Count -ne 3) { throw 'Native player report has missing or invalid native sample fields: coordinates.' }
                foreach ($coordinate in $vector) {
                    if (-not (Test-ObpPlayerTrialNumber $coordinate)) { throw 'Native player report has missing or invalid native sample fields: coordinates.' }
                }
            }
        }
    }
    if ($null -ne $baseline -and $trace -cne $baseline) { throw 'Native player trace differs between 30 and 120 rendering FPS.' }
    $baseline = $trace
}
Write-Host '[player-capabilities] PASS: identical native traces at 30 and 120 rendering FPS' -ForegroundColor Green
exit 0
