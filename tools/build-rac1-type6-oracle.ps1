# Research-only executable. No retail input or upstream source is copied into the repository.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReferenceRoot,
    [Parameter(Mandatory)][string]$Cxx,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$compiler = (Resolve-Path -LiteralPath $Cxx).Path
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$arguments = @()
if ([IO.Path]::GetFileNameWithoutExtension($compiler) -eq 'zig') { $arguments += 'c++' }
$arguments += @('-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness', '-I', $reference,
    (Join-Path $PSScriptRoot 'rac1-type6-native-oracle.cpp'),
    (Join-Path $reference 'runtime/ee/ee_interpreter.cpp'),
    (Join-Path $reference 'runtime/vu/vu_interpreter.cpp'), '-o', $output)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Type-6 research oracle compilation failed.' }
Write-Host 'Built type-6 research oracle. Set OBP_RAC1_TYPE6_ORACLE to this executable for the retail comparison tests.'
