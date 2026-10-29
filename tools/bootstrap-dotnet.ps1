#Requires -Version 5.1
# Install the SDK from global.json and the net8.0 runtime locally.
# Does not change system PATH, SDK installation, or machine configuration.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$policy = Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
$version = $policy.sdk.version

function Test-ObpDotnet([string]$exe) {
  try {
    & $exe --version 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { return $false }
    $runtimes = @(& $exe --list-runtimes 2>&1)
    return ($LASTEXITCODE -eq 0 -and @($runtimes -match '^Microsoft\.NETCore\.App 8\.0\.').Count -gt 0)
  }
  catch { return $false }
}

Push-Location $root
try {
  if ((Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-ObpDotnet 'dotnet')) {
    Write-Output "Compatible .NET SDK $(& dotnet --version) and .NET 8 runtime already available."
    return
  }

  $dest = Join-Path $root '.tools/dotnet'
  $localDotnet = Join-Path $dest 'dotnet.exe'
  if ((Test-Path $localDotnet) -and (Test-ObpDotnet $localDotnet)) {
    Write-Output "Compatible local .NET SDK $(& $localDotnet --version) and runtime already in $dest."
  }
  else {
    New-Item -ItemType Directory -Force $dest | Out-Null
    $installer = Join-Path $dest 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version $version -InstallDir $dest -NoPath
    if ($LASTEXITCODE -ne 0) { throw "Installing SDK $version failed." }
    & $installer -Runtime dotnet -Channel '8.0' -InstallDir $dest -NoPath
    if ($LASTEXITCODE -ne 0) { throw "Installing .NET 8 runtime failed." }
    if (-not (Test-ObpDotnet $localDotnet)) { throw "The locally installed toolchain failed validation." }
    Write-Output "Installed .NET SDK $version and .NET 8 runtime in $dest."
  }
  Write-Output 'For this PowerShell session, run:'
  Write-Output ('  $env:DOTNET_ROOT = "{0}"' -f $dest)
  Write-Output '  $env:PATH = "$env:DOTNET_ROOT;$env:PATH"'
}
finally {
  Pop-Location
}
