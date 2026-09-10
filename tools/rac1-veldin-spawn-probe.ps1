param(
    [Parameter(Mandatory = $true)]
    [string] $IsoPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    node tools/rac1-veldin-spawn-probe.mjs $IsoPath
    if ($LASTEXITCODE -ne 0) { throw "spawn executable probe failed: $LASTEXITCODE" }

    $env:OBP_RAC1_ISO = $IsoPath
    dotnet test tests/OBP.Tests/OBP.Tests.csproj `
        -c Release --no-restore `
        --filter 'FullyQualifiedName~Rac1VeldinSpawnTests'
    if ($LASTEXITCODE -ne 0) { throw "spawn retail tests failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
}
