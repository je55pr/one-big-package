#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'obp-native-process.ps1')

function Test-ObpSmokeLog {
    param([int]$ExitCode, [string]$Log, [string]$CompletionMarker)
    if ([string]::IsNullOrWhiteSpace($CompletionMarker)) { throw 'A smoke completion marker is required.' }
    $failure = '(?im)^\s*(?:(?:\[[^\]\r\n]+\]|20\d{2}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s*)*' +
        '(?:ERROR\s*:|SCRIPT ERROR\s*:|SHADER ERROR\s*:|Unhandled\s+exception\b|FATAL(?:\s+ERROR)?\b|' +
        'E\s+\d+:\d{2}:\d{2}(?::\d+|\.\d+)?(?:\s*:|\s+)|' +
        '(?:System\.)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*Exception\s*:|\[[^\]\r\n]+\]\s+FAIL\b)' +
        '|ObjectDB instances (?:were )?leaked|resources still in use at exit|^Leaked instance:|quit preparation failed'
    return $ExitCode -eq 0 -and $Log.Contains($CompletionMarker) -and -not [regex]::IsMatch($Log, $failure)
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
    $code = Invoke-ObpNativeProcessCapture -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory `
        -StandardOutputPath $stdout -StandardErrorPath $stderr
    $output = [IO.File]::ReadAllText($stdout)
    $errors = [IO.File]::ReadAllText($stderr)
    Get-Content -LiteralPath $stdout -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    Get-Content -LiteralPath $stderr -Encoding UTF8 | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { return $code }
    if (-not (Test-ObpSmokeLog -ExitCode $code -Log ($output + "`n" + $errors) -CompletionMarker $CompletionMarker)) {
        Write-Host "Smoke did not complete cleanly. Native stdout/stderr: $logs" -ForegroundColor Red
        return 2
    }
    return 0
}
