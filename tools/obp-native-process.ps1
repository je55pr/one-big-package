#Requires -Version 5.1

# Capture native streams without converting stderr into PowerShell errors.
# Callers still decide which exit codes and reports constitute success.
function Invoke-ObpNativeProcessCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$StandardOutputPath,
        [Parameter(Mandatory = $true)][string]$StandardErrorPath,
        [string]$WorkingDirectory = (Get-Location).Path,
        [scriptblock]$OnOutput
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
    $started = $false
    $stdoutWriter = $null
    $stderrWriter = $null
    try {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        if ($OnOutput) {
            $stdoutWriter = [IO.StreamWriter]::new($StandardOutputPath, $false, $utf8)
            $stderrWriter = [IO.StreamWriter]::new($StandardErrorPath, $false, $utf8)
            $stdoutWriter.AutoFlush = $true
            $stderrWriter.AutoFlush = $true
        }
        [void]$process.Start()
        $started = $true
        if ($OnOutput) {
            # Service both asynchronous reads from this PowerShell runspace.
            # Event callbacks on native threads cannot safely invoke script blocks.
            $stdoutRead = $process.StandardOutput.ReadLineAsync()
            $stderrRead = $process.StandardError.ReadLineAsync()
            while ($null -ne $stdoutRead -or $null -ne $stderrRead) {
                if ($null -ne $stdoutRead -and $stdoutRead.IsCompleted) {
                    $line = $stdoutRead.GetAwaiter().GetResult()
                    if ($null -eq $line) { $stdoutRead = $null }
                    else {
                        $stdoutWriter.WriteLine($line)
                        & $OnOutput $line | Out-Null
                        $stdoutRead = $process.StandardOutput.ReadLineAsync()
                    }
                }
                if ($null -ne $stderrRead -and $stderrRead.IsCompleted) {
                    $line = $stderrRead.GetAwaiter().GetResult()
                    if ($null -eq $line) { $stderrRead = $null }
                    else {
                        $stderrWriter.WriteLine($line)
                        & $OnOutput $line | Out-Null
                        $stderrRead = $process.StandardError.ReadLineAsync()
                    }
                }
                $pending = @($stdoutRead, $stderrRead) | Where-Object { $null -ne $_ }
                if ($pending) { [Threading.Tasks.Task]::WaitAny([Threading.Tasks.Task[]]@($pending), 100) | Out-Null }
            }
            while (-not $process.WaitForExit(100)) { }
            return $process.ExitCode
        }
        # Drain both pipes concurrently; a full stderr pipe must not deadlock stdout.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(100)) { }
        [IO.File]::WriteAllText($StandardOutputPath, $stdout.GetAwaiter().GetResult(), $utf8)
        [IO.File]::WriteAllText($StandardErrorPath, $stderr.GetAwaiter().GetResult(), $utf8)
        return $process.ExitCode
    }
    finally {
        # Pipeline termination (including Ctrl+C) runs finally, but not catch.
        # Cleanup failures must not replace the original callback/pipeline error.
        if ($started) {
            try {
                if (-not $process.HasExited) {
                    if ($PSVersionTable.PSEdition -eq 'Core') { $process.Kill($true) }
                    else { $process.Kill() }
                    [void]$process.WaitForExit(5000)
                }
            }
            catch [InvalidOperationException] { } # The process may exit between HasExited and Kill.
            catch { Write-Warning -WarningAction Continue "Native child cleanup failed: $($_.Exception.Message)" }
        }
        if ($stdoutWriter) { $stdoutWriter.Dispose() }
        if ($stderrWriter) { $stderrWriter.Dispose() }
        $process.Dispose()
    }
}
