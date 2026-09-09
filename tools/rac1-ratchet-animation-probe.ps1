param(
    [string]$IsoPath = 'C:\ChatGPT\ISOs\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso',
    [string]$OutputPath,
    [string]$TraceOutputPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($IsoPath)) {
    $IsoPath = 'C:\ChatGPT\ISOs\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso'
}
$repo = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $PSScriptRoot 'rac1-ratchet-animation-probe.mjs'
if (-not $OutputPath) {
    $OutputPath = Join-Path $repo 'research\generated\rac1-ratchet-animation-states.json'
}
if (-not $TraceOutputPath) {
    $TraceOutputPath = Join-Path $repo 'research\generated\rac1-ratchet-animation-trace.json'
}
if (-not (Test-Path -LiteralPath $IsoPath)) { throw "ISO not found: $IsoPath" }

& node $probe --iso $IsoPath --out $OutputPath --trace-out $TraceOutputPath
if ($LASTEXITCODE -ne 0) { throw "probe failed with exit code $LASTEXITCODE" }
Get-Content -Raw -LiteralPath $OutputPath | ConvertFrom-Json | Out-Null
Get-Content -Raw -LiteralPath $TraceOutputPath | ConvertFrom-Json | Out-Null
Write-Host "Wrote $OutputPath"
Write-Host "Wrote $TraceOutputPath"
Write-Host 'RAC1 Ratchet animation static probe: PASS'
