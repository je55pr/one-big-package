#Requires -Version 5.1

# Capture native streams without converting stderr into PowerShell errors.
# Callers still decide which exit codes and reports constitute success.
function Invoke-ObpNativeProcessCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$StandardOutputPath,
        [Parameter(Mandatory = $true)][string]$StandardErrorPath,
        [string]$WorkingDirectory = (Get-Location).Path
    )
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FilePath
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    # Windows CRT quoting preserves spaces, literal quotes and trailing slashes.
    $quoted = $ArgumentList | ForEach-Object { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }
    $start.Arguments = $quoted -join ' '
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        # Drain both pipes concurrently; a full stderr pipe must not deadlock stdout.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($StandardOutputPath, $stdout.GetAwaiter().GetResult(), $utf8)
        [IO.File]::WriteAllText($StandardErrorPath, $stderr.GetAwaiter().GetResult(), $utf8)
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}
