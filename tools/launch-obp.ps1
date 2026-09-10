#Requires -Version 5.1
<#
.SYNOPSIS
  Launch One Big Package in "Default mode" for showing people: the gamey title
  screen, then the Game Sources / world browser. Opens maximised.

.DESCRIPTION
  This is the production-showcase entry point the desktop shortcut points at.
  It quietly builds the local Godot game assembly (Debug), makes sure Godot + the asset import
  cache are in place, then starts the app windowed-maximised with no developer
  console and no test-scene arguments. Pass -SkipBuild to launch faster when
  you know the build is current.

.EXAMPLE
  ./tools/launch-obp.ps1
  ./tools/launch-obp.ps1 -SkipBuild
#>
param(
  [switch]$SkipBuild,
  [switch]$Windowed
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host ''
Write-Host '  One Big Package - starting production showcase build...' -ForegroundColor Cyan
Write-Host ''

# --- pinned Godot ----------------------------------------------------------
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$consoleExe = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path $consoleExe)) {
  Write-Host '  Fetching the pinned Godot build (one time)...' -ForegroundColor DarkGray
  & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
# Run the game through the window-subsystem exe so no console lingers behind it.
$gameExe = $consoleExe -replace '_console\.exe$', '.exe'
if (-not (Test-Path $gameExe)) { $gameExe = $consoleExe }

# --- build --------------------------------------------------------------
if (-not $SkipBuild) {
  Write-Host '  Building...' -ForegroundColor DarkGray
  # An unexported Godot C# project loads .godot/mono/temp/bin/Debug.
  # Building Release here can appear to work only when a stale Debug cache already exists.
  & dotnet build (Join-Path $root 'game/OneBigPackage.csproj') -c Debug --nologo -v quiet
  if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Warning 'Build failed - see the errors above.'
    Read-Host 'Press Enter to close'
    exit $LASTEXITCODE
  }
}

# Keep the asset import cache current (title-screen logo, window icon, ...).
Write-Host '  Preparing assets...' -ForegroundColor DarkGray
& $consoleExe --headless --path (Join-Path $root 'game') --import 2>&1 | Out-Null

# --- run --------------------------------------------------------------------
$gameArgs = @('--path', (Join-Path $root 'game'))
if (-not $Windowed) { $gameArgs += '--maximized' }

Write-Host '  Launching.' -ForegroundColor Green
Start-Process -FilePath $gameExe -ArgumentList $gameArgs -WorkingDirectory $root
Start-Sleep -Milliseconds 400
