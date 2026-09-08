#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Rac1Iso = $env:OBP_RAC1_ISO,
    [string]$GcIso = $env:OBP_GC_ISO,
    [string]$UyaIso = $env:OBP_UYA_ISO,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$authorities = [ordered]@{
    OBP_RAC1_ISO = $Rac1Iso
    OBP_GC_ISO   = $GcIso
    OBP_UYA_ISO  = $UyaIso
}

foreach ($entry in $authorities.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($entry.Value)) {
        throw "$($entry.Key) is required. Pass the matching parameter or set the environment variable."
    }
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
        throw "$($entry.Key) does not point to a readable file: $($entry.Value)"
    }
}
$previous = @{}
$exitCode = 1
try {
    foreach ($entry in $authorities.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }

    & dotnet test (Join-Path $root 'OneBigPackage.sln') -c $Configuration --nologo
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($entry in $authorities.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $previous[$entry.Key], 'Process')
    }
}

if ($exitCode -ne 0) { exit $exitCode }
Write-Host 'Retail-authority test suite passed.'
