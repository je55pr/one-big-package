#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')

function Test-ObpSmokeLog {
    param([int]$ExitCode, [string]$Log, [string]$CompletionMarker)
    if ([string]::IsNullOrWhiteSpace($CompletionMarker)) { throw 'A smoke completion marker is required.' }
    return $ExitCode -eq 0 -and $Log.Contains($CompletionMarker) -and -not (Test-ObpSmokeFailure -Log $Log)
}

function Test-ObpSmokeFailure {
    param([string]$Log)
    $failure = '(?im)^\s*(?:(?:\[[^\]\r\n]+\]|20\d{2}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s*)*' +
        '(?:ERROR\s*:|SCRIPT ERROR\s*:|SHADER ERROR\s*:|Unhandled\s+exception\b|FATAL(?:\s+ERROR)?\b|' +
        'E\s+\d+:\d{2}:\d{2}(?::\d+|\.\d+)?(?:\s*:|\s+)|' +
        '(?:System\.)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*Exception(?:\s*:|\s*$)|\[[^\]\r\n]+\]\s+FAIL\b)' +
        '|ObjectDB instances (?:were )?leaked|resources still in use at exit|^Leaked instance:|RIDs? of type [^\r\n]* (?:was|were) leaked|quit preparation failed'
    return [regex]::IsMatch($Log, $failure)
}

function Write-ObpSmokeOutput {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Windows PowerShell 5.1 transcripts omit Write-Information. Native logs and failure paths must remain visible without adding success-stream objects.')]
    param([string]$Message)
    if ($Message) { Write-Host $Message.TrimEnd() }
}

function Remove-ObpSmokeHistory {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
    param([Parameter(Mandatory = $true)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $completed = @(Get-ChildItem -LiteralPath $rootPath -Directory | Where-Object {
        $_.Name -match '^[a-f0-9]{32}$' -and $_.Parent.FullName -eq $rootPath -and
        -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName '.complete') -PathType Leaf)
    } | Sort-Object LastWriteTimeUtc -Descending)
    # Keep the newest 50 completed histories for at most 14 days, regardless of
    # outcome. Active histories are ineligible for deletion.
    for ($index = 0; $index -lt $completed.Count; $index++) {
        if ($index -ge 50 -or $completed[$index].LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-14)) {
            $path = $completed[$index].FullName
            if ($PSCmdlet.ShouldProcess($path, 'Remove expired completed smoke history')) {
                try { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop }
                catch {
                    # A partial deletion must remain eligible for a later retry.
                    try {
                        if (Test-Path -LiteralPath $path -PathType Container) {
                            [IO.File]::WriteAllText((Join-Path $path '.complete'), 'cleanup-incomplete')
                            # Restoring the marker must not promote old evidence into the newest fifty.
                            (Get-Item -LiteralPath $path).LastWriteTimeUtc = $completed[$index].LastWriteTimeUtc
                        }
                    }
                    catch { Write-Verbose 'Could not restore the completed-history marker.' }
                    Write-Warning -WarningAction Continue "Could not remove completed smoke history $path. Continuing retention."
                }
            }
        }
    }
}

function Invoke-ObpSmokeProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$CompletionMarker,
        [string]$WorkingDirectory = (Get-Location).Path,
        [ValidateRange(1, 2147483647)][int]$TimeoutSeconds = 900
    )
    $historyRoot = $env:OBP_SMOKE_RESULTS_ROOT
    if ([string]::IsNullOrWhiteSpace($historyRoot)) { $historyRoot = Join-Path $PSScriptRoot '../.build/smoke-results' }
    $history = [IO.Path]::GetFullPath($historyRoot)
    $logs = Join-Path $history ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $stdout = Join-Path $logs 'stdout.log'
    $stderr = Join-Path $logs 'stderr.log'
    Write-ObpSmokeOutput "Smoke native stdout/stderr: $logs"
    try {
        $code = Invoke-ObpNativeProcessCapture -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory `
        -StandardOutputPath $stdout -StandardErrorPath $stderr -TimeoutSeconds $TimeoutSeconds -OnOutput { param($line) Write-ObpSmokeOutput $line }
    }
    finally {
        # Native cleanup has finished before this run becomes eligible for retention.
        try {
            [IO.File]::WriteAllText((Join-Path $logs '.complete'), 'completed')
            Remove-ObpSmokeHistory -Root $history
        }
        catch { Write-Warning -WarningAction Continue "Smoke log retention failed: $($_.Exception.Message)" }
    }
    $output = [IO.File]::ReadAllText($stdout)
    $errors = [IO.File]::ReadAllText($stderr)
    if ($code -ne 0) {
        Write-ObpSmokeOutput "Smoke native process exited with code $code. Native stdout/stderr: $logs"
        return $code
    }
    if (-not (Test-ObpSmokeLog -ExitCode $code -Log ($output + "`n" + $errors) -CompletionMarker $CompletionMarker)) {
        Write-ObpSmokeOutput "Smoke did not complete cleanly. Native stdout/stderr: $logs"
        return 2
    }
    return 0
}
