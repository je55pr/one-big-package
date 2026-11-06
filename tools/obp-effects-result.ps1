#Requires -Version 5.1
. (Join-Path $PSScriptRoot 'obp-smoke-process.ps1')

function Assert-ObpEffectsPngChunks([IO.Stream]$Stream) {
    # Complete structural PNG evidence: ordered critical chunks, bounded lengths
    # and every CRC, including IDAT and the terminating IEND.
    $table = New-Object uint32[] 256
    for ($index = 0; $index -lt 256; $index++) {
        [uint32]$value = $index
        for ($bit = 0; $bit -lt 8; $bit++) {
            if ($value -band 1) { $value = [uint32](($value -shr 1) -bxor [uint32]3988292384) }
            else { $value = $value -shr 1 }
        }
        $table[$index] = $value
    }
    $reader = [IO.BinaryReader]::new($Stream, [Text.Encoding]::ASCII, $true)
    try {
        $Stream.Position = 8; $first = $true; $dataSeen = $false
        while ($Stream.Position -lt $Stream.Length) {
            if ($Stream.Length - $Stream.Position -lt 12) { throw 'Effects render has incomplete PNG chunks.' }
            $bytes = $reader.ReadBytes(4)
            [long]$length = ([long]$bytes[0] -shl 24) + ([long]$bytes[1] -shl 16) + ([long]$bytes[2] -shl 8) + $bytes[3]
            $kind = $reader.ReadBytes(4); $name = [Text.Encoding]::ASCII.GetString($kind)
            if ($length -gt [int]::MaxValue -or $length -gt $Stream.Length - $Stream.Position - 4) { throw 'Effects render has incomplete PNG chunks.' }
            if (($first -and ($name -cne 'IHDR' -or $length -ne 13)) -or (!$first -and $name -ceq 'IHDR')) {
                throw 'Effects render has invalid PNG chunk order.'
            }
            $data = $reader.ReadBytes([int]$length)
            [uint32]$crc = 4294967295
            foreach ($byte in $kind) { $crc = [uint32](($crc -shr 8) -bxor $table[($crc -bxor $byte) -band 255]) }
            foreach ($byte in $data) { $crc = [uint32](($crc -shr 8) -bxor $table[($crc -bxor $byte) -band 255]) }
            $crc = [uint32]($crc -bxor [uint32]4294967295)
            $bytes = $reader.ReadBytes(4)
            [long]$expected = ([long]$bytes[0] -shl 24) + ([long]$bytes[1] -shl 16) + ([long]$bytes[2] -shl 8) + $bytes[3]
            if ($crc -ne $expected) { throw 'Effects render has an invalid PNG chunk checksum.' }
            if ($name -ceq 'IDAT' -and $length -gt 0) { $dataSeen = $true }
            if ($name -ceq 'IEND') {
                if (!$dataSeen -or $length -ne 0 -or $Stream.Position -ne $Stream.Length) { throw 'Effects render has invalid PNG chunk completion.' }
                return
            }
            $first = $false
        }
        throw 'Effects render has incomplete PNG chunks.'
    }
    finally { $reader.Dispose() }
}

function Read-ObpEffectsTape([string]$Path, [int]$StopFrame) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw 'Effects recording is missing or empty.'
    }
    $file = [IO.File]::OpenRead($Path)
    $gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionMode]::Decompress)
    $reader = [IO.StreamReader]::new($gzip)
    $steps = 0; $processFrames = 0; $physicsFrames = 0; $checks = 0; $ended = $false
    $commands = @()
    try {
        $header = $reader.ReadLine() | ConvertFrom-Json
        if ($header.Header.Version -ne 2 -or $header.Header.Destination -cne 'obp:fusion-lab') { throw 'Effects tape has no qualified fusion-lab header.' }
        while ($null -ne ($line = $reader.ReadLine())) {
            $entry = $line | ConvertFrom-Json
            if ($ended) { throw 'Effects tape has data after its end.' }
            if ($null -ne $entry.Step) {
                $step = $entry.Step
                if ($step.Index -ne $steps -or $step.Kind -notin @(0, 1)) { throw 'Effects tape steps are incomplete or out of order.' }
                if ($step.Kind -eq 1) { $processFrames++ } else { $physicsFrames++ }
                if ($null -ne $step.Checkpoint) { $checks++ }
                foreach ($command in @($step.Commands)) {
                    if ($command.Kind -eq 'effects') { $commands += ('{0}:{1}' -f ($processFrames - 1), $command.Value) }
                }
                $steps++
            }
            elseif ($null -ne $entry.End) {
                if ($entry.End.Reason -cne 'saved' -or $entry.End.Steps -ne $steps) { throw 'Effects tape was not completely saved.' }
                $ended = $true
            }
            else { throw 'Effects tape contains an unexpected entry.' }
        }
        if (!$ended -or $processFrames -ne ($StopFrame + 1) -or $physicsFrames -eq 0 -or $checks -eq 0 -or
            ($commands -join ',') -cne '1:start,30:reset,40:start') {
            throw "Effects tape did not complete its requested native windows: ended=$ended, process=$processFrames expected=$($StopFrame + 1), physics=$physicsFrames, checkpoints=$checks, commands=$($commands -join ',')."
        }
        return @{ Steps = $steps; Checkpoints = $checks }
    }
    finally { $reader.Dispose(); $gzip.Dispose(); $file.Dispose() }
}

function Assert-ObpEffectsResult([string]$Mode, [string]$Directory, [string]$Log, [int]$StopFrame) {
    if ((Test-ObpSmokeFailure -Log $Log) -or $Log -match '(?im)\[playtest\]\s+(?:DIVERGED|[^\r\n]*failed)') {
        throw "Effects $Mode emitted a failure or leaked resources."
    }
    if ($Mode -eq 'render') {
        if (!$Log.Contains('[effects-smoke] PASS:')) { throw 'Effects render did not complete.' }
        $reportPath = Join-Path $Directory 'report.json'
        if (!(Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'Effects render is missing its fresh report.json.' }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $names = @('fusion-lab.png', 'rac1-LEVEL0.png', 'rac2-LEVEL1.png', 'rac3-TABLE1.png', 'obp-fusion-lab.png')
        if ($report.Version -ne 1 -or $report.Passed -cne $true -or @($report.Samples).Count -ne 14 -or @($report.Captures).Count -ne 5 -or
            @(Compare-Object $names @($report.Captures | ForEach-Object File)).Count -ne 0) {
            throw "Effects render report is incomplete: version=$($report.Version) expected=1, passed=$($report.Passed), samples=$(@($report.Samples).Count) expected=14, captures=$(@($report.Captures).Count) expected=5."
        }
        foreach ($capture in $report.Captures) {
            $image = Join-Path $Directory $capture.File
            if ($capture.Width -le 0 -or $capture.Height -le 0 -or !(Test-Path -LiteralPath $image -PathType Leaf) -or
                (Get-Item -LiteralPath $image).Length -eq 0 -or
                $capture.ChangedPixels.rac1 -lt 100 -or $capture.ChangedPixels.rac2 -lt 100 -or $capture.ChangedPixels.rac3 -lt 100) {
                throw 'Effects render lacks its captured trilogy images.'
            }
            $file = [IO.File]::OpenRead($image)
            try {
                $header = New-Object byte[] 24
                $read = $file.Read($header, 0, $header.Length)
                $signature = [BitConverter]::ToString($header, 0, 16)
                $width = ([long]$header[16] -shl 24) + ([long]$header[17] -shl 16) + ([long]$header[18] -shl 8) + $header[19]
                $height = ([long]$header[20] -shl 24) + ([long]$header[21] -shl 16) + ([long]$header[22] -shl 8) + $header[23]
                if ($read -ne 24 -or $signature -cne '89-50-4E-47-0D-0A-1A-0A-00-00-00-0D-49-48-44-52' -or
                    $width -ne $capture.Width -or $height -ne $capture.Height) { throw 'Effects render lacks a valid PNG header matching its reported dimensions.' }
                Assert-ObpEffectsPngChunks -Stream $file
            }
            finally { $file.Dispose() }
        }
        return
    }
    $tape = Read-ObpEffectsTape (Join-Path $Directory 'effects.jsonl.gz') $StopFrame
    if ($Mode -eq 'record') {
        $saved = [regex]::Match($Log, '\[playtest\] SAVED [^\r\n]+; steps=(\d+); markers=\d+')
        if (!$saved.Success -or [int]$saved.Groups[1].Value -ne $tape.Steps -or
            !$Log.Contains('reason=playtest-record-smoke-pass exitCode=0')) { throw 'Effects recording did not complete.' }
        return
    }
    if (!$Log.Contains('[playtest] PASS;') -or !$Log.Contains('reason=playtest-replay-complete exitCode=0')) { throw 'Effects replay did not complete.' }
    $reportPath = Join-Path $Directory 'replay.json'
    if (!(Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'Effects replay is missing its fresh replay.json.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.Result -cne 'matched' -or $null -ne $report.Error -or $null -ne $report.FirstDifference -or
        # Replay also compares the header's initial state, before the step checkpoints.
        $report.Steps -ne $tape.Steps -or $report.CheckpointsCompared -ne ($tape.Checkpoints + 1)) {
        throw "Effects replay report is incomplete or diverged: result=$($report.Result), error=$($report.Error), steps=$($report.Steps) expected=$($tape.Steps), checkpoints=$($report.CheckpointsCompared) expected=$($tape.Checkpoints + 1)."
    }
}
