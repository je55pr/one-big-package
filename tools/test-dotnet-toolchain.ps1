#Requires -Version 5.1
# Offline cross-platform regression checks for the shared PowerShell toolchain policy.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'dotnet-toolchain.ps1')
$required = (Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
if ($required -ne '10.0.403') { throw "Update toolchain tests for SDK $required." }

foreach ($good in @('10.0.403', '10.0.404', '10.0.499')) {
  if (-not (Test-ObpSdkVersion $good $required)) { throw "Rejected valid SDK $good." }
}
foreach ($bad in @('8.0.427', '10.0.402', '10.0.500', '10.0.400', '11.0.100', '10.0.403-preview', 'abc')) {
  if (Test-ObpSdkVersion $bad $required) { throw "Accepted invalid SDK $bad." }
}
if (-not (Test-ObpRuntimeList @('Microsoft.NETCore.App 8.0.33 [/mock]', 'Microsoft.NETCore.App 10.0.14 [/mock]'))) {
  throw 'Rejected .NET 8 runtime.'
}
if (Test-ObpRuntimeList @('Microsoft.NETCore.App 10.0.14 [/mock]')) { throw 'Accepted missing .NET 8 runtime.' }

$dir = Join-Path ([IO.Path]::GetTempPath()) ('obp-toolchain-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $dir | Out-Null
try {
  if ($env:OS -eq 'Windows_NT') {
    $fake = Join-Path $dir 'dotnet.cmd'
    Set-Content $fake -Encoding ASCII -Value @(
      '@echo off',
      'if "%~1"=="--version" echo %OBP_FAKE_SDK%',
      'if "%~1"=="--list-runtimes" if "%OBP_FAKE_RUNTIME%"=="1" echo Microsoft.NETCore.App 8.0.33 [/mock]',
      'if "%~1"=="build" echo %CD%>> "%OBP_FAKE_CWD_LOG%"',
      'if "%~1"=="test" echo %CD%>> "%OBP_FAKE_CWD_LOG%"',
      'if "%~1"=="run" echo %CD%>> "%OBP_FAKE_CWD_LOG%"',
      'exit /b 0'
    )
  }
  else {
    $fake = Join-Path $dir 'dotnet'
    Set-Content $fake -Encoding ASCII -Value @(
      '#!/usr/bin/env bash',
      'case "$1" in',
      '  --version) echo "$OBP_FAKE_SDK" ;;',
      '  --list-runtimes) [ "$OBP_FAKE_RUNTIME" = "1" ] && echo "Microsoft.NETCore.App 8.0.33 [/mock]" ;;',
      '  build|test|run) pwd >> "$OBP_FAKE_CWD_LOG" ;;',
      'esac'
    )
    & chmod +x $fake
  }
  $env:OBP_FAKE_SDK = '10.0.403'
  $env:OBP_FAKE_RUNTIME = '1'
  if (-not (Test-ObpDotnet $fake $required)) { throw 'Valid fake SDK/runtime rejected.' }
  $env:OBP_FAKE_SDK = '10.0.402'
  if (Test-ObpDotnet $fake $required) { throw 'Old SDK accepted.' }
  $env:OBP_FAKE_SDK = '10.0.403'
  $env:OBP_FAKE_RUNTIME = '0'
  if (Test-ObpDotnet $fake $required) { throw 'Missing runtime accepted.' }

  # Wrappers must select global.json from the repository, not the caller's cwd.
  $sandbox = Join-Path $dir 'repo'
  $mockTools = Join-Path $sandbox 'tools'
  New-Item -ItemType Directory -Force $mockTools | Out-Null
  Copy-Item (Join-Path $PSScriptRoot 'build.ps1') $mockTools
  Copy-Item (Join-Path $PSScriptRoot 'test.ps1') $mockTools
  $env:OBP_FAKE_CWD_LOG = Join-Path $dir 'wrapper-cwds'
  $oldPath = $env:PATH
  $env:PATH = "$dir$([IO.Path]::PathSeparator)$oldPath"
  Push-Location $dir
  try {
    & (Join-Path $mockTools 'build.ps1') -Configuration Release
    & (Join-Path $mockTools 'test.ps1') -Configuration Release
  }
  finally {
    Pop-Location
    $env:PATH = $oldPath
  }
  $seen = @(Get-Content $env:OBP_FAKE_CWD_LOG)
  if ($seen.Count -ne 3 -or @($seen | Where-Object { $_ -ne $sandbox }).Count -gt 0) {
    throw "Build/test wrappers did not select their repository root: $seen"
  }
  Write-Output 'PowerShell SDK/runtime and wrapper policy checks passed.'
}
finally {
  Remove-Item Env:OBP_FAKE_SDK,Env:OBP_FAKE_RUNTIME,Env:OBP_FAKE_CWD_LOG -ErrorAction SilentlyContinue
  Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}
