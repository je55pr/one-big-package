#Requires -Version 5.1
<#
.SYNOPSIS
  Bootstrap a checkout with the SDK from global.json and pinned Godot .NET.
  If the SDK is missing, run ./tools/bootstrap-dotnet.ps1 first.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Prefer an SDK bootstrapped locally, without modifying machine PATH.
$localDotnet = Join-Path $root '.tools/dotnet'
if (Test-Path (Join-Path $localDotnet 'dotnet.exe')) {
  Push-Location $root
  try {
    $localVersion = & (Join-Path $localDotnet 'dotnet.exe') --version 2>&1
    $useLocal = $LASTEXITCODE -eq 0
  }
  catch { $useLocal = $false }
  finally { Pop-Location }
  if ($useLocal) {
    $env:DOTNET_ROOT = $localDotnet
    $env:PATH = "$localDotnet;$env:PATH"
  }
}

Push-Location $root
try {
  & (Join-Path $PSScriptRoot 'check-toolchain.ps1')
  if ($LASTEXITCODE -ne 0) { throw "Toolchain validation failed." }

  $godot = & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Select-Object -Last 1

  & dotnet restore (Join-Path $root 'OneBigPackage.sln')
  if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
  & dotnet build (Join-Path $root 'OneBigPackage.sln') -c Debug --nologo
  if ($LASTEXITCODE -ne 0) { throw "Build failed." }

  # Sync script identities; this skips import/build when sidecars exist.
  & dotnet run --project (Join-Path $PSScriptRoot 'OBP.GodotUidTool/OBP.GodotUidTool.csproj') -- --sync --root $root --godot $godot
  if ($LASTEXITCODE -ne 0) { throw "Godot UID sync failed." }
  & $godot --headless --path (Join-Path $root 'game') --editor --import --quit
  if ($LASTEXITCODE -ne 0) { throw "Godot import failed." }

  Write-Host "`nBootstrap complete. Next: ./tools/test.ps1 ; ./tools/capture.ps1 smoke"
}
finally {
  Pop-Location
}
