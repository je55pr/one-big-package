#Requires -Version 5.1
# Source-authorized rendered effect acceptance and fresh-process input replay.
[CmdletBinding()]
param(
    [string]$Godot,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$Rac2Iso = $env:OBP_GC_ISO,
    [string]$Rac3Iso = $env:OBP_UYA_ISO,
    [string]$OutputDirectory,
    [ValidateSet('all', 'render', 'record', 'replay')][string]$Mode = 'all',
    [ValidateRange(65, 600)][int]$RecordingStopFrame = 65,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'obp-effects-result.ps1')
$root = Split-Path -Parent $PSScriptRoot
if (!$Godot) {
    $manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
    $Godot = Join-Path $root ('.tools/godot/' + $manifest.godot.artifacts.win64.exe)
}
if (!(Test-Path -LiteralPath $Godot -PathType Leaf)) { throw 'Provide the pinned Godot .NET editor or run bootstrap-godot.ps1.' }
foreach ($source in @($Rac1Iso, $Rac2Iso, $Rac3Iso)) {
    if ([string]::IsNullOrWhiteSpace($source) -or !(Test-Path -LiteralPath $source -PathType Leaf)) { throw 'All three authorized original sources are required.' }
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'captures/effects-285' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
if ($Mode -in @('all', 'record') -and (Test-Path -LiteralPath (Join-Path $output 'effects.jsonl.gz'))) {
    throw 'Choose a new OutputDirectory for a fresh recording. Existing runs are preserved.'
}
if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'sync-godot-uids.ps1') -Check
    if ($LASTEXITCODE -ne 0) { throw 'UID coverage check failed.' }
    & dotnet build (Join-Path $root 'game/OBP.Game.csproj') -c Debug --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
}
function Invoke-EffectsRun([string]$RunName, [string[]]$ExtraArguments) {
    # Only remove this mode's known reports/images so stale output cannot certify a new process.
    $fresh = if ($RunName -eq 'render') { @('report.json', 'fusion-lab.png', 'rac1-LEVEL0.png', 'rac2-LEVEL1.png', 'rac3-TABLE1.png', 'obp-fusion-lab.png') }
        elseif ($RunName -eq 'replay') { @('replay.json') } else { @() }
    foreach ($name in $fresh) {
        $path = Join-Path $output $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path }
    }
    Write-Host "Running effects $RunName acceptance..."
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = [IO.Path]::GetFullPath($Godot)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['OBP_EFFECT_REPLAY_STOP_FRAME'] = $RecordingStopFrame.ToString([Globalization.CultureInfo]::InvariantCulture)
    $arguments = @('--path', (Join-Path $root 'game'), '--windowed', '--position', '-20000,-20000', '--rendering-method', 'gl_compatibility', '--rendering-driver', 'opengl3',
        '--fixed-fps', '60', '--max-fps', '60', '--resolution', '960x540', '--', '--effects-smoke', '--fixed-seed', '1234', '--no-profile',
        '--source-config', (Join-Path $output 'sources.private.json'), '--rac1-iso', $Rac1Iso, '--rac2-iso', $Rac2Iso, '--rac3-iso', $Rac3Iso) + $ExtraArguments
    # Windows CRT quoting also works on the repository's Windows PowerShell 5.1 baseline.
    $quoted = $arguments | ForEach-Object { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
    $start.Arguments = $quoted -join ' '
    $process = $null
    $outPath = Join-Path $output "$RunName.stdout.log"
    $errPath = Join-Path $output "$RunName.stderr.log"
    $outFile = $null; $errFile = $null
    $outCopy = $null; $errCopy = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $outFile = [IO.FileStream]::new($outPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
        $errFile = [IO.FileStream]::new($errPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
        $process = [Diagnostics.Process]::Start($start)
        $outCopy = $process.StandardOutput.BaseStream.CopyToAsync($outFile)
        $errCopy = $process.StandardError.BaseStream.CopyToAsync($errFile)
        while (!$process.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalMinutes -gt 11) {
                throw "Effects $RunName run timed out."
            }
        }
        $outCopy.GetAwaiter().GetResult() | Out-Null
        $errCopy.GetAwaiter().GetResult() | Out-Null
        $exitCode = $process.ExitCode
    }
    finally {
        try {
            if ($process -and !$process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
        }
        catch [InvalidOperationException] { Write-Verbose 'Effects child exited during cleanup.' }
        catch { Write-Warning -WarningAction Continue "Effects child cleanup failed: $($_.Exception.Message)" }
        finally {
            $copies = @($outCopy, $errCopy) | Where-Object { $null -ne $_ }
            if ($copies) { try { [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($copies), 5000) } catch { Write-Verbose 'Effects output drain ended during cleanup.' } }
            if ($outFile) { $outFile.Dispose() }
            if ($errFile) { $errFile.Dispose() }
            if ($process) { $process.Dispose() }
        }
    }
    Get-Content -LiteralPath $outPath -Tail 5
    if ($exitCode -ne 0) { Get-Content -LiteralPath $errPath -Tail 25; throw "Effects $RunName failed ($exitCode)." }
    Assert-ObpEffectsResult -Mode $RunName -Directory $output -StopFrame $RecordingStopFrame `
        -Log (([IO.File]::ReadAllText($outPath)) + "`n" + ([IO.File]::ReadAllText($errPath)))
}
if ($Mode -in @('all', 'render')) { Invoke-EffectsRun 'render' @('--acceptance-out', (Join-Path $output 'report.json')) }
if ($Mode -in @('all', 'record')) { Invoke-EffectsRun 'record' @('--destination', 'obp:fusion-lab', '--record-playtest', (Join-Path $output 'effects.jsonl.gz'), '--playtest-report', (Join-Path $output 'record.json')) }
if ($Mode -in @('all', 'replay')) { Invoke-EffectsRun 'replay' @('--destination', 'obp:fusion-lab', '--replay-playtest', (Join-Path $output 'effects.jsonl.gz'), '--playtest-report', (Join-Path $output 'replay.json')) }
Write-Host 'Effects acceptance passed. Captures, replay and local source configuration remain in the ignored output directory.'
exit 0
