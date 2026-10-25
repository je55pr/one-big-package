#Requires -Version 5.1
<#
.SYNOPSIS
  Resolve the pinned engine and build the app for a source-backed smoke.
  Returns only the engine path; the caller owns its mode, input and assertions.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Sources,
    [ValidateSet(1, 3)][int]$SourceCount = 3,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if ($Sources.Count -ne $SourceCount) { throw 'Supply every source path required by this smoke.' }
foreach ($source in $Sources) {
    if (-not $source -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw 'All required authorised original source files must be available.'
    }
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolchain = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$engine = Join-Path $repoRoot ".tools/godot/$($toolchain.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the pinned Godot engine.' }
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Game -WarningsAsErrors | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Godot C# build failed.' }
}
return $engine
