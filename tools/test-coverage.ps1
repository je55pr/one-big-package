#Requires -Version 5.1
<#
.SYNOPSIS
  Collect public-source-only managed coverage and publish validated, source-relative reports.
#>
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = '.build/coverage',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$reportRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory)) }
$runId = [guid]::NewGuid().ToString('N')
$raw = Join-Path $repoRoot ('.build/coverage-raw/' + $runId)
$report = Join-Path $reportRoot $runId
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$latest = Join-Path $reportRoot 'latest.json'
@{ Version = 1; Status = 'running'; Run = $runId } | ConvertTo-Json | Set-Content -LiteralPath $latest -Encoding UTF8
$saved = @{}
# Never inherit authorised discs, emulator/oracle RAM or evidence-update switches.
Get-ChildItem Env: | Where-Object Name -like 'OBP_*' | ForEach-Object {
    $saved[$_.Name] = $_.Value
    [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process')
}
try {
    if (-not $NoBuild) {
        & dotnet build (Join-Path $repoRoot 'OneBigPackage.sln') -c $Configuration --nologo -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Coverage build failed.' }
    }
    & dotnet run --project (Join-Path $PSScriptRoot 'OBP.GodotUidTool/OBP.GodotUidTool.csproj') -c $Configuration --no-build -- --check --root $repoRoot
    if ($LASTEXITCODE -ne 0) { throw 'Godot UID coverage failed.' }
    & dotnet test (Join-Path $repoRoot 'tests/OBP.Tests/OBP.Tests.csproj') -c $Configuration --no-build --no-restore `
        --settings (Join-Path $PSScriptRoot 'coverage.runsettings') --collect 'XPlat Code Coverage' --results-directory $raw --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Portable tests failed.' }
    $cobertura = @(Get-ChildItem -LiteralPath $raw -Recurse -Filter 'coverage.cobertura.xml' -File)
    $openCover = @(Get-ChildItem -LiteralPath $raw -Recurse -Filter 'coverage.opencover.xml' -File)
    if ($cobertura.Count -ne 1 -or $openCover.Count -ne 1) { throw 'Fresh coverage reports are missing or ambiguous; instrumentation did not complete.' }
    & dotnet run --project (Join-Path $PSScriptRoot 'OBP.CoverageTool/OBP.CoverageTool.csproj') -c $Configuration --no-build -- `
        $repoRoot $cobertura[0].FullName $openCover[0].FullName $report
    if ($LASTEXITCODE -ne 0) { throw 'Coverage scope/path/report validation failed.' }
    @{ Version = 1; Status = 'measured'; Run = $runId; Summary = ($runId + '/summary.json'); HumanReport = ($runId + '/index.html') } |
        ConvertTo-Json | Set-Content -LiteralPath $latest -Encoding UTF8
    $summary = Get-Content -LiteralPath (Join-Path $report 'summary.md') -Raw
    Write-Host $summary
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary -Encoding UTF8
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "`nDownload the portable-coverage artifact and open index.html for per-file results.`n" -Encoding UTF8
    }
    Write-Host "Coverage reports: $report"
}
catch {
    # Never replace missing collection with a zero percentage or leave a stale success pointer.
    @{ Version = 1; Status = 'failed'; Run = $runId; Failure = 'collection-or-validation-failed' } |
        ConvertTo-Json | Set-Content -LiteralPath $latest -Encoding UTF8
    throw
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
