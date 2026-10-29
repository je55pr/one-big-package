#Requires -Version 5.1
# Shared, side-effect-free SDK/runtime policy for Windows bootstrap and diagnostics.
function Test-ObpSdkVersion {
  param([string]$Candidate, [string]$Required)
  if ($Candidate -notmatch '^\d+\.\d+\.\d+$' -or $Required -notmatch '^\d+\.\d+\.\d+$') { return $false }
  $found = @($Candidate.Split('.') | ForEach-Object { [int]$_ })
  $minimum = @($Required.Split('.') | ForEach-Object { [int]$_ })
  return ($found[0] -eq $minimum[0] -and $found[1] -eq $minimum[1] -and
    [math]::Floor($found[2] / 100) -eq [math]::Floor($minimum[2] / 100) -and
    $found[2] -ge $minimum[2])
}

function Test-ObpRuntimeList {
  param([string[]]$Runtimes)
  return @($Runtimes -match '^Microsoft\.NETCore\.App 8\.0\.').Count -gt 0
}

function Test-ObpDotnet {
  param([string]$Executable, [string]$Required)
  try {
    $sdk = (& $Executable --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-ObpSdkVersion $sdk $Required)) { return $false }
    $runtimes = @(& $Executable --list-runtimes 2>&1)
    return ($LASTEXITCODE -eq 0 -and (Test-ObpRuntimeList $runtimes))
  }
  catch { return $false }
}
