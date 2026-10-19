# Shared startup arguments: apply saved placement before the native window is shown.
function Get-ObpWindowArguments {
    param([switch]$Windowed)
    $settings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Godot/app_userdata/One Big Package/window-placement.json'
    try {
        if (Test-Path -LiteralPath $settings -PathType Leaf) {
            $placement = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
            $width = [int]$placement.Width
            $height = [int]$placement.Height
            $x = [int]$placement.X
            $y = [int]$placement.Y
            if ($width -gt 0 -and $width -le 32768 -and $height -gt 0 -and $height -le 32768) {
                if ($null -ne $placement.Screen -and [int]$placement.Screen -ge 0) {
                    '--screen'
                    [string][int]$placement.Screen
                }
                if ($placement.Maximized -and -not $Windowed) { '--maximized' } else { '--windowed' }
                '--position'
                "$x,$y"
                '--resolution'
                "${width}x${height}"
                return
            }
        }
    }
    catch { Write-Verbose "Ignoring unreadable window placement: $_" }
    '--windowed'
}
