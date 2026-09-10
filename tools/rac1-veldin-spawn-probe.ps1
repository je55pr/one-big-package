param(
    [Parameter(Mandatory = $true)]
    [string] $IsoPath,

    [string] $EeMemoryPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $nodeArgs = @('tools/rac1-veldin-spawn-probe.mjs', $IsoPath)
    if (-not [string]::IsNullOrWhiteSpace($EeMemoryPath)) {
        $nodeArgs += @('--ee-memory', $EeMemoryPath)
    }
    node @nodeArgs
    if ($LASTEXITCODE -ne 0) { throw "spawn evidence probe failed: $LASTEXITCODE" }

    $env:OBP_RAC1_ISO = $IsoPath
    dotnet test tests/OBP.Tests/OBP.Tests.csproj `
        -c Release --no-restore `
        --filter 'FullyQualifiedName~Rac1VeldinSpawnTests'
    if ($LASTEXITCODE -ne 0) { throw "spawn retail tests failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
}
