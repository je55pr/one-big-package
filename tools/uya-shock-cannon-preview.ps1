#Requires -Version 5.1
# Isolated source-asset preview; does not modify OBPGame or the user's saves.
param(
    [Alias('UyaIso')][string]$Rac3Iso = $env:OBP_UYA_ISO,
    [switch]$Smoke,
    [switch]$Rendered,
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
if ($Capture -and $Smoke -and -not $Rendered) { throw 'Use -Rendered when capturing a smoke run.' }
$root = Split-Path -Parent $PSScriptRoot
if (-not $Rac3Iso -or -not (Test-Path -LiteralPath $Rac3Iso -PathType Leaf)) { throw 'An authorized original NTSC-U UYA ISO is required.' }
$manifest = Get-Content (Join-Path $PSScriptRoot 'godot-toolchain.json') -Raw | ConvertFrom-Json
$godot = Join-Path $root ".tools/godot/$($manifest.godot.artifacts.win64.exe)"
if (-not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'bootstrap-godot.ps1') -Platform win64 | Out-Null
}
$project = Join-Path $root 'captures/uya-shock-cannon-preview'
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/uya-shock-cannon-preview/Preview.cs') -Destination $project
@'
config_version=5
[application]
config/name="UYA Shock Cannon source preview"
run/main_scene="res://preview.tscn"
[display]
window/size/viewport_width=1280
window/size/viewport_height=720
window/size/no_focus=true
[dotnet]
project/assembly_name="UyaShockCannonPreview"
[rendering]
renderer/rendering_method="gl_compatibility"
'@ | Set-Content -LiteralPath (Join-Path $project 'project.godot') -Encoding utf8
@'
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://Preview.cs" id="1"]
[node name="UyaShockCannonPreview" type="Node"]
script = ExtResource("1")
'@ | Set-Content -LiteralPath (Join-Path $project 'preview.tscn') -Encoding utf8
$rac3 = [System.Security.SecurityElement]::Escape((Join-Path $root 'src/OBP.RAC3/OBP.RAC3.csproj'))
$adapter = [System.Security.SecurityElement]::Escape((Join-Path $root 'src/OBP.Godot/OBP.Godot.csproj'))
$sdkVersion = $manifest.godot.version.Split('-')[0]
@"
<Project Sdk="Godot.NET.Sdk/$sdkVersion">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDynamicLoading>true</EnableDynamicLoading></PropertyGroup>
  <ItemGroup><ProjectReference Include="$rac3"/><ProjectReference Include="$adapter"/></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $project 'UyaShockCannonPreview.csproj') -Encoding utf8
& dotnet build (Join-Path $project 'UyaShockCannonPreview.csproj') --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$previousIso = $env:OBP_UYA_ISO
$previousCapture = $env:OBP_UYA_PREVIEW_CAPTURE
try {
    $env:OBP_UYA_ISO = (Resolve-Path -LiteralPath $Rac3Iso).Path
    if ($Capture) { $env:OBP_UYA_PREVIEW_CAPTURE = Join-Path $project 'preview.png' }
    [string[]]$windowArgs = if ($Smoke -and -not $Rendered) { @('--headless') }
        elseif ($Smoke) { @('--windowed', '--position', '-20000,-20000') }
        else { @('--windowed') }
    [string[]]$userArgs = if ($Smoke) { @('--', '--smoke') } else { @() }
    & $godot @windowArgs '--path' $project @userArgs
    $result = $LASTEXITCODE
}
finally {
    $env:OBP_UYA_ISO = $previousIso
    $env:OBP_UYA_PREVIEW_CAPTURE = $previousCapture
}
exit $result
