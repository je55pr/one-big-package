#Requires -Version 5.1
# Run the OBP native test suite (xUnit).
param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& dotnet run --project (Join-Path $PSScriptRoot 'OBP.GodotUidTool/OBP.GodotUidTool.csproj') -- --check --root $root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet test (Join-Path $root 'OneBigPackage.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
