#Requires -Version 5.1
# Retail-backed Godot exit regression. Godot often exits code 0 even when ObjectDB leaks.
# No ISO, decoded audio or local paths are written to the repository.
param(
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$GodotPath,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
if (-not $GodotPath) {
    $GodotPath = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Pinned Godot executable unavailable. Run tools/bootstrap-godot.ps1 or supply -GodotPath.'
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The Godot C# build failed.' }
}

$sourceArgs = @()
if ($GcIso) {
    if (-not (Test-Path -LiteralPath $GcIso -PathType Leaf)) {
        throw 'The selected R&C2 ISO is not accessible.'
    }
    $sourceArgs += @('--gc-iso', $GcIso)
}

function Invoke-AudioExitProbe([string]$label, [string[]]$gameArgs, [int]$expectedWorlds, [bool]$expectStress) {
    $arguments = @(
        '--verbose', '--headless', '--path', (Join-Path $root 'game'),
        '--rendering-method', 'gl_compatibility',
        '--quit-after', '3600', '--'
    ) + $sourceArgs + $gameArgs

    # Capture stderr too: Godot prints ObjectDB leaks there but returns success.
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $GodotPath @arguments 2>&1 | ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $priorPreference
    }
    $log = $lines -join "`n"
    $worlds = [regex]::Matches($log, '\[destinations\] world ready').Count
    $audio = [regex]::Matches($log, '\[audio\] playing [1-9][0-9]* level audio source\(s\)').Count
    $leaked = [regex]::IsMatch($log, '(?im)^Leaked instance:|^WARNING:.*ObjectDB instances were leaked')
    $shutdownError = [regex]::IsMatch($log, '(?im)^ERROR:|^Unhandled exception')
    $finished = $log.Contains('[lifecycle] root-exit')
    $stressFinished = $log.Contains('[stress] ================ summary ================')

    if ($exitCode -ne 0 -or $worlds -lt $expectedWorlds -or $audio -lt $expectedWorlds -or
        $leaked -or $shutdownError -or -not $finished -or ($expectStress -and -not $stressFinished)) {
        $relevant = $lines | Where-Object {
            $_ -match 'Leaked instance|ObjectDB instances were leaked|^ERROR:|^Unhandled exception|\[audio\] playing|\[destinations\] world ready|\[stress\]|\[lifecycle\]'
        }
        $relevant | ForEach-Object { Write-Host $_ }
        throw "FAIL $label (exit=$exitCode worlds=$worlds audio=$audio leaked=$leaked shutdownError=$shutdownError rootExit=$finished stressFinished=$stressFinished)"
    }
    Write-Host "[audio-shutdown-smoke] PASS $label ($worlds worlds, $audio level-audio starts; no ObjectDB leaks)"
}

Invoke-AudioExitProbe 'Oozla shutdown' @('--destination', 'rac2:LEVEL1') 1 $false
# Repeatedly unload and reload the same live audio world through the normal host path.
Invoke-AudioExitProbe 'three Oozla world switches' @('--stress-switch', 'oozla,oozla,oozla') 3 $true
Write-Host '[audio-shutdown-smoke] PASS'
