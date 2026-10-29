#Requires -Version 5.1
# Diagnose the selected SDK and C# language version from the repo root.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$policy = Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
$required = [version]$policy.sdk.version

Push-Location $root
try {
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is not on PATH. Install SDK $required with ./tools/bootstrap-dotnet.ps1 (or use the .NET 8 SDK installer)."
  }
  try {
    $sdk = (& dotnet --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw $sdk }
  }
  catch {
    throw "Cannot select the SDK from global.json ($required, $($policy.sdk.rollForward)). Run ./tools/bootstrap-dotnet.ps1 and add .tools/dotnet to your session PATH. Details: $_"
  }

  $selected = [version]$sdk
  if ($selected.Major -ne $required.Major -or $selected.Minor -ne $required.Minor -or
      [math]::Floor($selected.Build / 100) -ne [math]::Floor($required.Build / 100) -or
      $selected -lt $required) {
    throw "Unexpected SDK $sdk. global.json requires $required or a later patch in its feature band."
  }

  $languageOutput = (& dotnet msbuild (Join-Path $root 'src/OBP.Core/OBP.Core.csproj') -getProperty:LangVersion -nologo 2>&1 | Out-String).Trim()
  $msbuildExitCode = $LASTEXITCODE
  $language = ($languageOutput -split '\r?\n' | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim()
  if ($msbuildExitCode -ne 0 -or $language -ne '12.0') {
    throw "Expected C# LangVersion 12.0; MSBuild reported '$language'. Check Directory.Build.props."
  }
  $runtimes = @(& dotnet --list-runtimes 2>&1)
  if ($LASTEXITCODE -ne 0 -or @($runtimes -match '^Microsoft\.NETCore\.App 8\.0\.').Count -eq 0) {
    throw "The .NET 8 runtime needed to execute net8.0 tools/tests is missing. Run ./tools/bootstrap-dotnet.ps1."
  }
  Write-Output "Toolchain OK: .NET SDK $sdk (global.json: >= $required, $($policy.sdk.rollForward)); C# $language; .NET 8 runtime present"
}
finally {
  Pop-Location
}
