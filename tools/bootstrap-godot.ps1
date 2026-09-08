#Requires -Version 5.1
<#
.SYNOPSIS
  Download and verify the pinned Godot .NET editor into .tools/godot/ (git-ignored).
.DESCRIPTION
  Reads tools/godot-toolchain.json, downloads the matching Godot archive, checks
  its SHA-512, extracts it, and prints the editor path. Idempotent: re-running
  with the archive already present and verified does nothing.
#>
param(
  [ValidateSet('win64', 'linux_x86_64')]
  [string]$Platform = 'win64',
  [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $repoRoot '.tools'
$godotDir = Join-Path $toolsDir 'godot'
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$art = $manifest.godot.artifacts.$Platform

New-Item -ItemType Directory -Force -Path $godotDir | Out-Null
$archivePath = Join-Path $godotDir $art.file
$exePath = Join-Path $godotDir $art.exe

function Test-Sha512([string]$path, [string]$expected) {
  if (-not (Test-Path $path)) { return $false }
  $actual = (Get-FileHash -Algorithm SHA512 -Path $path).Hash.ToLower()
  return $actual -eq $expected.ToLower()
}

if ((Test-Path $exePath) -and -not $Force) {
  Write-Host "Godot $($manifest.godot.version) already present: $exePath"
  Write-Output $exePath
  return
}

if ($Force -or -not (Test-Sha512 $archivePath $art.sha512)) {
  $url = "$($manifest.godot.baseUrl)/$($art.file)"
  Write-Host "Downloading $url"
  Invoke-WebRequest -Uri $url -OutFile $archivePath -UseBasicParsing
}

if (-not (Test-Sha512 $archivePath $art.sha512)) {
  throw "SHA-512 mismatch for $archivePath - expected $($art.sha512)"
}
Write-Host "SHA-512 OK: $($art.file)"

Expand-Archive -Path $archivePath -DestinationPath $godotDir -Force
if (-not (Test-Path $exePath)) { throw "Extraction did not produce $exePath" }
Write-Host "Godot $($manifest.godot.version) ready: $exePath"
Write-Output $exePath
