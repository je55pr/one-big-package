# Source-authorized rendered effect acceptance and fresh-process input replay.
[CmdletBinding()]
param(
    [string]$Godot,
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$Rac2Iso = $env:OBP_GC_ISO,
    [string]$Rac3Iso = $env:OBP_UYA_ISO,
    [string]$OutputDirectory,
    [ValidateSet('all', 'render', 'record', 'replay')][string]$Mode = 'all',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
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
    Write-Host "Running effects $RunName acceptance..."
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = [IO.Path]::GetFullPath($Godot)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $arguments = @('--path', (Join-Path $root 'game'), '--rendering-method', 'gl_compatibility', '--rendering-driver', 'opengl3',
        '--fixed-fps', '60', '--max-fps', '60', '--resolution', '960x540', '--', '--effects-smoke', '--fixed-seed', '1234', '--no-profile',
        '--source-config', (Join-Path $output 'sources.private.json'), '--rac1-iso', $Rac1Iso, '--rac2-iso', $Rac2Iso, '--rac3-iso', $Rac3Iso) + $ExtraArguments
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $outPath = Join-Path $output "$RunName.stdout.log"
    $errPath = Join-Path $output "$RunName.stderr.log"
    $outFile = [IO.FileStream]::new($outPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    $errFile = [IO.FileStream]::new($errPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    $outCopy = $process.StandardOutput.BaseStream.CopyToAsync($outFile)
    $errCopy = $process.StandardError.BaseStream.CopyToAsync($errFile)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        while (!$process.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalMinutes -gt 11) { $process.Kill($true); throw "Effects $RunName run timed out." }
        }
        $outCopy.GetAwaiter().GetResult() | Out-Null
        $errCopy.GetAwaiter().GetResult() | Out-Null
    }
    finally { $outFile.Dispose(); $errFile.Dispose() }
    Get-Content -LiteralPath $outPath -Tail 5
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $errPath -Tail 25; throw "Effects $RunName failed ($($process.ExitCode))." }
    if (Select-String -LiteralPath $errPath -Pattern 'SHADER ERROR|SCRIPT ERROR|ERROR:|Unhandled exception' -Quiet) {
        Get-Content -LiteralPath $errPath -Tail 25; throw "Effects $RunName emitted engine errors."
    }
}
if ($Mode -in @('all', 'render')) { Invoke-EffectsRun 'render' @('--acceptance-out', (Join-Path $output 'report.json')) }
if ($Mode -in @('all', 'record')) { Invoke-EffectsRun 'record' @('--destination', 'obp:fusion-lab', '--record-playtest', (Join-Path $output 'effects.jsonl.gz'), '--playtest-report', (Join-Path $output 'record.json')) }
if ($Mode -in @('all', 'replay')) { Invoke-EffectsRun 'replay' @('--destination', 'obp:fusion-lab', '--replay-playtest', (Join-Path $output 'effects.jsonl.gz'), '--playtest-report', (Join-Path $output 'replay.json')) }
Write-Host 'Effects acceptance passed. Captures, replay and local source configuration remain in the ignored output directory.'
