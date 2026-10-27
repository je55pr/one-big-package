# Research only; retail input and the external MIT interpreter remain outside Git.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReferenceRoot,
    [Parameter(Mandatory)][string]$Cxx,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
# Qualify the actual compiler inputs, including dirty checkouts and worktrees;
# HEAD alone can differ while these files still match the pinned revision.
if (Test-Path -LiteralPath (Join-Path $reference '.git')) {
    $expectedRevision = '3fd14c12e4dff2fd4b6661a6e54187faea3f76c8'
    $interpreterInputs = @('runtime/ee/ee_interpreter.cpp', 'runtime/ee/ee_interpreter.h', 'runtime/ee/ps2_fpu.h',
        'runtime/kernel/ee_memory.h', 'runtime/kernel/ee_types.h', 'runtime/vu/vu_interpreter.cpp',
        'runtime/vu/vu_interpreter.h', 'runtime/vu/vu_xgkick.h')
    & git -C $reference diff --quiet --no-ext-diff $expectedRevision -- @interpreterInputs
    if ($LASTEXITCODE -ne 0) { throw 'Interpreter inputs differ from the documented pin, or its Git object is unavailable.' }
}
$compiler = (Get-Command -Name $Cxx -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$arguments = @()
if ([IO.Path]::GetFileNameWithoutExtension($compiler) -eq 'zig') { $arguments += 'c++' }
$arguments += @('-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness', '-I', $reference,
    (Join-Path $PSScriptRoot 'rac2-museum-native-oracle.cpp'),
    (Join-Path $reference 'runtime/ee/ee_interpreter.cpp'),
    (Join-Path $reference 'runtime/vu/vu_interpreter.cpp'), '-o', $output)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'GC Museum research oracle compilation failed.' }
Write-Host 'Built GC Museum oracle. Set OBP_RAC2_MUSEUM_ORACLE to this executable for retail comparisons.'
