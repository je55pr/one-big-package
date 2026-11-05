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

function Invoke-ObpSmokeProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$CompletionMarker,
        [string]$WorkingDirectory = (Get-Location).Path
    )
    $logs = Join-Path $PSScriptRoot ('../.build/smoke-results/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $stdout = Join-Path $logs 'stdout.log'
    $stderr = Join-Path $logs 'stderr.log'
    Write-ObpSmokeOutput "Smoke native stdout/stderr: $logs"
    $code = Invoke-ObpNativeProcessCapture -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory `
        -StandardOutputPath $stdout -StandardErrorPath $stderr -OnOutput { param($line) Write-ObpSmokeOutput $line }
    $output = [IO.File]::ReadAllText($stdout)
    $errors = [IO.File]::ReadAllText($stderr)
    if ($code -ne 0) { return $code }
    if (-not (Test-ObpSmokeLog -ExitCode $code -Log ($output + "`n" + $errors) -CompletionMarker $CompletionMarker)) {
        Write-ObpSmokeOutput "Smoke did not complete cleanly. Native stdout/stderr: $logs"
        return 2
    }
    return 0
}
