#Requires -Version 5.1
<#
.SYNOPSIS
  One-shot dev bootstrap: check the .NET SDK, fetch the pinned Godot, restore
  and build, and generate the Godot import cache. After this:
    ./tools/build.ps1 ; ./tools/test.ps1 ; ./tools/capture.ps1 smoke
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json

$sdk = (& dotnet --version)
$major = [int]($sdk.Split('.')[0])
if ($major -lt $manifest.dotnet.minimumSdkMajor) {
  throw "Need .NET SDK $($manifest.dotnet.minimumSdkMajor).x or newer; found $sdk. Install from https://dotnet.microsoft.com/download"
}
Write-Host ".NET SDK $sdk OK (target $($manifest.dotnet.targetFramework))"

$godot = & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Select-Object -Last 1

& dotnet restore (Join-Path $root 'OneBigPackage.sln')
& dotnet build (Join-Path $root 'OneBigPackage.sln') -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

& $godot --headless --path (Join-Path $root 'game') --import
Write-Host "`nBootstrap complete. Next: ./tools/test.ps1 ; ./tools/capture.ps1 smoke"
