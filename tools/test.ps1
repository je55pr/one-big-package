#Requires -Version 5.1
# Run the OBP native test suite (xUnit).
param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& dotnet test (Join-Path $root 'OneBigPackage.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
