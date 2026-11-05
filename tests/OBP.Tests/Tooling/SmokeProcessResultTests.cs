using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OBP.Tests;

public sealed class SmokeProcessResultTests
{
    private const string Marker = "[lifecycle] quit-request reason=rac1-crate-smoke-pass exitCode=0";

    public static IEnumerable<object[]> LogCases()
    {
        foreach (string shell in new[] { "powershell.exe", "pwsh" })
        {
            yield return [shell, "clean", 0, Marker, true];
            yield return [shell, "nonzero", 17, Marker, false];
            yield return [shell, "incomplete", 0, "[crate-smoke] started", false];
            yield return [shell, "wrong-scenario", 0, Marker.Replace("crate", "enemy"), false];
            foreach (string problem in new[]
            {
                "ERROR: callback failed", "SCRIPT ERROR: invalid call", "SHADER ERROR: invalid shader",
                "Unhandled exception. System.InvalidDataException", "System.ArgumentException: invalid sample",
                "Unhandled exception. System.InvalidOperationException: injected", "System.InvalidDataException",
                "2026-10-29 12:35:20 ERROR: callback failed", "E 0:00:04:123: engine error",
                "WARNING: 2 ObjectDB instances were leaked at exit (run with --verbose for details).",
                "WARNING: 1 resources still in use at exit.", "Leaked instance: RefCounted:123",
                "WARNING: 1 RID of type \"CanvasItem\" was leaked.", "WARNING: 2 RIDs of type \"CanvasItem\" were leaked.",
                "[crate-smoke] FAIL: late failure", "[lifecycle] quit preparation failed: injected",
            })
                yield return [shell, problem, 0, Marker + "\n" + problem, false];
        }
    }

    [SkippableTheory]
    [MemberData(nameof(LogCases))]
    public async Task CompletionRequiresItsOwnMarkerAndRejectsLateErrors(string shell, string label, int code, string log, bool expected)
    {
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            . {{Quote(Path.Combine(RepoPaths.Root, "tools/obp-smoke-process.ps1"))}}
            $actual = Test-ObpSmokeLog -ExitCode {{code}} -Log {{Quote(log)}} -CompletionMarker {{Quote(Marker)}}
            if ($actual -ne ${{expected.ToString().ToLowerInvariant()}}) { throw 'Incorrect smoke outcome.' }
            """;
        var result = await Run(RequireShell(shell), script);
        Assert.True(result.Code == 0, label + ": " + result.Error + result.Output);
    }

    public static IEnumerable<object[]> WrapperCases()
    {
        foreach (string shell in new[] { "powershell.exe", "pwsh" })
            foreach (var (script, reason, destinations, noProfile, stateMode) in new[]
            {
            ("rac1-crate-smoke.ps1", "rac1-crate-smoke-pass", "rac1:LEVEL0,rac1:LEVEL1", true, false),
            ("rac1-placed-bolt-smoke.ps1", "rac1-placed-bolt-smoke-pass", "rac1:LEVEL0,rac1:LEVEL8,rac1:LEVEL11", true, false),
            ("rac1-campaign-smoke.ps1", "rac1-campaign-smoke-pass", ",", true, false),
            ("rac1-metropolis-taxi-smoke.ps1", "rac1-metropolis-taxi-smoke-pass", "rac1:LEVEL3", true, false),
            ("movement-smoke.ps1", "movement-smoke-pass", "rac1:LEVEL0,rac2:LEVEL1,rac3:TABLE1", false, false),
            ("rac1-enemy-smoke.ps1", "rac1-enemy-smoke-pass", "rac1:LEVEL0", false, false),
            ("rac1-frog-smoke.ps1", "rac1-frog-smoke-pass", "rac1:LEVEL6", false, false),
            ("rac1-wrench-smoke.ps1", "rac1-wrench-smoke-pass", "rac1:LEVEL0", false, false),
            ("rac1-bomb-glove-smoke.ps1", "rac1-bomb-glove-smoke-pass", "rac1:LEVEL0", false, false),
            ("rac1-bomb-glove-smoke.ps1", "rac1-bomb-glove-repro-pass", "rac1:LEVEL0", false, true),
            ("rac1-veldin-play-smoke.ps1", "rac1-veldin-play-smoke-pass", "rac1:LEVEL0", false, false),
            ("rac1-combat-contract-smoke.ps1", "rac1-combat-contract-pass", "rac1:LEVEL0", false, false),
            ("controller-smoke.ps1", "controller-smoke-pass", "", false, false),
            ("rac1-player-audio-smoke.ps1", "rac1-player-audio-smoke-pass", "rac1:LEVEL0,rac1:LEVEL0", false, false),
        })
                foreach (var (fault, code) in new[] { ("clean", 0), ("incomplete", 2), ("leak", 2), ("nonzero", 17) })
                    yield return [shell, script, reason, destinations, noProfile, stateMode, fault, code];
        foreach (string shell in new[] { "powershell.exe", "pwsh" })
            foreach (string fault in new[] { "negative-rid", "negative-rids", "negative-shader", "negative-unhandled", "negative-engine", "negative-managed" })
                yield return [shell, "rac1-player-audio-smoke.ps1", "rac1-player-audio-smoke-pass", "rac1:LEVEL0,rac1:LEVEL0", false, false, fault, 1];
    }

    [SkippableTheory]
    [MemberData(nameof(WrapperCases))]
    public async Task WrappersCheckEachProcessAndPreserveArguments(string shell, string scriptName, string reason, string destinations, bool noProfile, bool stateMode, string fault, int expected)
    {
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp smoke result " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string tools = Path.Combine(directory, "tools"), source = Path.Combine(directory, "source with spaces.iso");
            string state = Path.Combine(directory, "state with spaces.json");
            Directory.CreateDirectory(tools);
            File.WriteAllText(source, "Synthetic prerequisite for the wrapper; never parsed as retail data.");
            if (stateMode) File.WriteAllText(state, "{}"); // Path forwarding only; state parsing has separate coverage.
            foreach (string name in new[] { scriptName, "obp-smoke-process.ps1" })
                File.Copy(Path.Combine(RepoPaths.Root, "tools", name), Path.Combine(tools, name));
            File.WriteAllText(Path.Combine(tools, "obp-window.ps1"), "function Get-ObpWindowArguments { return @('--headless') }");
            File.WriteAllText(Path.Combine(tools, "prepare-game-smoke.ps1"), "param([string[]]$Sources,[int]$SourceCount,[switch]$SkipBuild)\nreturn 'unused-engine'");
            File.WriteAllText(Path.Combine(tools, "godot-toolchain.json"), """{"godot":{"artifacts":{"win64":{"exe":"unused-engine"}}}}""");
            Directory.CreateDirectory(Path.Combine(directory, ".tools", "godot"));
            File.WriteAllText(Path.Combine(directory, ".tools", "godot", "unused-engine"), "");
            string calls = Path.Combine(directory, "calls.jsonl");
            string completion = Marker.Replace("rac1-crate-smoke-pass", reason);
            File.WriteAllText(Path.Combine(tools, "obp-native-process.ps1"), $$"""
                function Invoke-ObpNativeProcessCapture {
                    param($FilePath, [string[]]$ArgumentList, $WorkingDirectory, $StandardOutputPath, $StandardErrorPath)
                    if ($ArgumentList -contains '--rac1-bomb-glove-smoke' -and $env:OBP_BOMB_REPRO_STATE -cne {{(stateMode ? Quote(state) : "$null")}}) {
                        throw 'Bomb Glove state path was not forwarded through its environment boundary.'
                    }
                    Add-Content -LiteralPath {{Quote(calls)}} -Value (ConvertTo-Json -InputObject $ArgumentList -Compress)
                    if ($ArgumentList -contains '--test-scene') {
                        [IO.File]::WriteAllText($StandardOutputPath, "[diagnostic] rac1-player-audio-smoke/v1: OBP.Game.Diagnostics.DiagnosticUnavailableException: The audio fixture's player is unavailable.`n[lifecycle] quit-request reason=rac1-player-audio-smoke-fail exitCode=1")
                        $errorText = switch ({{Quote(fault)}}) {
                            'negative-rid' { 'WARNING: 1 RID of type "CanvasItem" was leaked.' }
                            'negative-rids' { 'WARNING: 2 RIDs of type "CanvasItem" were leaked.' }
                            'negative-shader' { 'SHADER ERROR: invalid shader' }
                            'negative-unhandled' { 'Unhandled exception. System.InvalidOperationException: injected' }
                            'negative-engine' { 'E 0:00:04:123: engine error' }
                            'negative-managed' { 'System.ArgumentException: invalid sample' }
                            default { '' }
                        }
                        [IO.File]::WriteAllText($StandardErrorPath, $errorText)
                        return 1
                    }
                    $text = if ({{Quote(fault)}} -eq 'incomplete') { '[fixture] started' } else { {{Quote(completion)}} }
                    [IO.File]::WriteAllText($StandardOutputPath, $text)
                    $errorText = if ({{Quote(fault)}} -eq 'leak') { 'WARNING: 2 ObjectDB instances were leaked at exit.' } else { '' }
                    [IO.File]::WriteAllText($StandardErrorPath, $errorText)
                    if ({{Quote(fault)}} -eq 'nonzero') { return 17 }
                    return 0
                }
                """);
            string sources = scriptName == "movement-smoke.ps1" ? $" -Rac2Iso {Quote(source)} -Rac3Iso {Quote(source)}" : "";
            string stateOption = stateMode ? $" -State {Quote(state)}" : "";
            var result = await Run(shell, $"& {Quote(Path.Combine(tools, scriptName))} -Rac1Iso {Quote(source)} -SkipBuild{sources}{stateOption}\nexit $LASTEXITCODE");
            Assert.True(result.Code == expected, result.Error + result.Output);
            if (fault.StartsWith("negative-", StringComparison.Ordinal))
                Assert.Contains("Static-camera audio availability regression failed", result.Error + result.Output);
            var arguments = File.ReadAllLines(calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray();
            string[] route = destinations.Split(',');
            Assert.Equal(expected == 0 || fault.StartsWith("negative-", StringComparison.Ordinal) ? route.Length : 1, arguments.Length);
            for (int index = 0; index < arguments.Length; index++)
            {
                var actual = arguments[index];
                Assert.Equal("--headless", actual[0]);
                if (noProfile) Assert.Contains("--no-profile", actual);
                Assert.Equal(Path.Combine(directory, "game"), actual[Array.IndexOf(actual, "--path") + 1]);
                Assert.Equal(source, actual[Array.IndexOf(actual, "--rac1-iso") + 1]);
                if (route[index].Length == 0) Assert.DoesNotContain("--destination", actual);
                else Assert.Equal(route[index], actual[Array.IndexOf(actual, "--destination") + 1]);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [SkippableTheory]
    [InlineData("powershell.exe", "clean", 0)]
    [InlineData("powershell.exe", "incomplete", 2)]
    [InlineData("powershell.exe", "leak", 2)]
    [InlineData("powershell.exe", "nonzero", 17)]
    [InlineData("pwsh", "clean", 0)]
    [InlineData("pwsh", "incomplete", 2)]
    [InlineData("pwsh", "leak", 2)]
    [InlineData("pwsh", "nonzero", 17)]
    public async Task NativeCaptureIncludesStderrAfterCompletionAndRetainsNonzeroExit(string shell, string fault, int expected)
    {
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp smoke native " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string child = Path.Combine(directory, "child.ps1");
            File.WriteAllText(child, $$"""
                param([string]$Message)
                [Console]::Out.WriteLine($Message)
                {{(fault == "incomplete" ? "" : "[Console]::Out.WriteLine(" + Quote(Marker) + ")")}}
                {{(fault == "leak" ? "[Console]::Error.WriteLine('WARNING: 2 ObjectDB instances were leaked at exit.')" : "")}}
                exit {{(fault == "nonzero" ? "17" : "0")}}
                """);
            const string message = "argument with spaces & apostrophe ' and quote \"";
            string transcript = Path.Combine(directory, "transcript.log");
            var result = await Run(shell, $"""
                $ErrorActionPreference = 'Stop'
                . {Quote(Path.Combine(RepoPaths.Root, "tools/obp-smoke-process.ps1"))}
                Start-Transcript -LiteralPath {Quote(transcript)} | Out-Null
                $code = Invoke-ObpSmokeProcess -FilePath {Quote(shell)} -ArgumentList @('-NoProfile','-NonInteractive','-File',{Quote(child)},'-Message',{Quote(message)}) -CompletionMarker {Quote(Marker)}
                Stop-Transcript | Out-Null
                exit $code
                """);
            Assert.True(result.Code == expected, result.Error + result.Output);
            Assert.Contains(message, result.Output);
            string recorded = File.ReadAllText(transcript);
            Assert.Contains(message, recorded);
            if (fault is "incomplete" or "leak") Assert.Contains("Smoke did not complete cleanly. Native stdout/stderr:", recorded);
            if (fault == "nonzero") Assert.Contains("Smoke native process exited with code 17. Native stdout/stderr:", recorded);
            if (fault == "leak")
            {
                Assert.Contains("WARNING: 2 ObjectDB instances were leaked at exit.", result.Output);
                Assert.Contains("WARNING: 2 ObjectDB instances were leaked at exit.", recorded);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [SkippableTheory]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    public async Task BothNativeStreamsRemainVisibleBeforeTheChildExits(string shell)
    {
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp live smoke " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string release = Path.Combine(directory, "release"), child = Path.Combine(directory, "child.ps1");
            File.WriteAllText(child, $$"""
                [Console]::Out.WriteLine('obp-live-stdout')
                [Console]::Error.WriteLine('obp-live-stderr')
                $deadline = [DateTime]::UtcNow.AddSeconds(15)
                while (-not (Test-Path -LiteralPath {{Quote(release)}})) {
                    if ([DateTime]::UtcNow -gt $deadline) { exit 71 }
                    Start-Sleep -Milliseconds 20
                }
                [Console]::Out.WriteLine({{Quote(Marker)}})
                exit 0
                """);
            var observed = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
            int released = 0;
            var result = await Run(shell, $"""
                $ErrorActionPreference = 'Stop'
                . {Quote(Path.Combine(RepoPaths.Root, "tools/obp-smoke-process.ps1"))}
                $code = Invoke-ObpSmokeProcess -FilePath {Quote(shell)} -ArgumentList @('-NoProfile','-NonInteractive','-File',{Quote(child)}) -CompletionMarker {Quote(Marker)}
                exit $code
                """, line =>
                {
                    if (line is "obp-live-stdout" or "obp-live-stderr") observed.TryAdd(line, 0);
                    if (observed.Count == 2 && Interlocked.CompareExchange(ref released, 1, 0) == 0)
                        File.WriteAllText(release, "Both streams observed before allowing the child to exit.");
                });
            Assert.True(result.Code == 0, result.Error + result.Output);
            Assert.Equal(2, observed.Count);
            Assert.Equal(1, released);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    [SkippableTheory]
    [InlineData("powershell.exe", "player-session-smoke.ps1", "clean", true)]
    [InlineData("pwsh", "player-session-smoke.ps1", "clean", true)]
    [InlineData("powershell.exe", "player-session-smoke.ps1", "leak", false)]
    [InlineData("pwsh", "player-session-smoke.ps1", "leak", false)]
    [InlineData("powershell.exe", "player-session-smoke.ps1", "error", false)]
    [InlineData("pwsh", "player-session-smoke.ps1", "error", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "clean", true)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "clean", true)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "leak", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "leak", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "error", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "error", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "stale", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "stale", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "empty", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "empty", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "partial", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "partial", false)]
    [InlineData("powershell.exe", "player-capabilities-smoke.ps1", "foreign-baseline", false)]
    [InlineData("pwsh", "player-capabilities-smoke.ps1", "foreign-baseline", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "clean", true)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "clean", true)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "leak", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "leak", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "error", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "error", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-rid-out", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-rid-out", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-rids-err", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-rids-err", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-shader-err", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-shader-err", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-unhandled-out", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-unhandled-out", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-engine-err", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-engine-err", false)]
    [InlineData("powershell.exe", "test-diagnostic-cli.ps1", "negative-managed-out", false)]
    [InlineData("pwsh", "test-diagnostic-cli.ps1", "negative-managed-out", false)]
    [InlineData("powershell.exe", "fusion-weapons-smoke.ps1", "clean", true)]
    [InlineData("pwsh", "fusion-weapons-smoke.ps1", "clean", true)]
    [InlineData("powershell.exe", "fusion-weapons-smoke.ps1", "leak", false)]
    [InlineData("pwsh", "fusion-weapons-smoke.ps1", "leak", false)]
    [InlineData("powershell.exe", "fusion-weapons-smoke.ps1", "error", false)]
    [InlineData("pwsh", "fusion-weapons-smoke.ps1", "error", false)]
    [InlineData("powershell.exe", "fusion-weapons-smoke.ps1", "record-log-error", false)]
    [InlineData("pwsh", "fusion-weapons-smoke.ps1", "record-log-error", false)]
    [InlineData("powershell.exe", "fusion-weapons-smoke.ps1", "replay-log-rid", false)]
    [InlineData("pwsh", "fusion-weapons-smoke.ps1", "replay-log-rid", false)]
    public async Task LifecycleRunnersRejectLeaksAndRequireFreshCompleteTrials(string shell, string runner, string fault, bool expected)
    {
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp lifecycle result " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string script = $$$$$"""
                $ErrorActionPreference = 'Stop'
                $root = {{{{{Quote(directory)}}}}}
                $tools = Join-Path $root 'tools'
                $out = Join-Path $root 'evidence'
                New-Item -ItemType Directory $tools,(Join-Path $root '.tools/godot') | Out-Null
                Copy-Item {{{{{Quote(Path.Combine(RepoPaths.Root, "tools", runner))}}}}} $tools
                Copy-Item {{{{{Quote(Path.Combine(RepoPaths.Root, "tools", "obp-smoke-process.ps1"))}}}}} $tools
                Set-Content (Join-Path $tools 'godot-toolchain.json') '{"godot":{"artifacts":{"win64":{"exe":"fake.ps1"}}}}'
                Set-Content (Join-Path $root 'source.iso') 'Synthetic prerequisite only; never decoded.'
                Set-Content (Join-Path $tools 'prepare-game-smoke.ps1') "return 'unused-engine'"
                $env:OBP_TEST_RUNNER_FAULT = {{{{{Quote(fault)}}}}}
                Set-Content (Join-Path $tools 'obp-native-process.ps1') @'
                function Invoke-ObpNativeProcessCapture {
                    param($FilePath,[string[]]$ArgumentList,$WorkingDirectory,$StandardOutputPath,$StandardErrorPath)
                    $pathIndex=[Array]::IndexOf($ArgumentList,'--path')
                    if($pathIndex -lt 0 -or $ArgumentList[$pathIndex+1] -ne (Join-Path $WorkingDirectory 'game')){throw 'Fixture received an invalid game path.'}
                    if($ArgumentList -contains '--player-session-smoke' -or $ArgumentList -contains '--player-capabilities-smoke' -or $ArgumentList -contains '--fusion-smoke'){
                        foreach($option in '--rac1-iso','--rac2-iso','--rac3-iso'){
                            $index=[Array]::IndexOf($ArgumentList,$option)
                            if($index -lt 0 -or $ArgumentList[$index+1] -ne (Join-Path $WorkingDirectory 'source.iso')){throw 'Fixture source argument boundary was lost.'}
                        }
                    }
                    $marker = '[player-session-smoke] PASS: fixture'
                    $code = 0
                    if ($ArgumentList -contains '--player-capabilities-smoke') {
                        $marker = '[player-capabilities-smoke] PASS: fixture'
                        $report = $ArgumentList[[Array]::IndexOf($ArgumentList,'--player-capabilities-out')+1]
                        if ($env:OBP_TEST_RUNNER_FAULT -ne 'stale') {
                            $samples = @(1..40 | ForEach-Object { @{Tick=$_*6;Clock=$_/10.0} })
                            $document = @{Version=1;Baselines=@{'rac2/item:30'=$samples;'rac3/item:43'=$samples}}
                            if ($env:OBP_TEST_RUNNER_FAULT -eq 'empty') { $document.Baselines=@{} }
                            if ($env:OBP_TEST_RUNNER_FAULT -eq 'partial') { $document.Baselines['rac2/item:30']=@($samples[0]) }
                            if ($env:OBP_TEST_RUNNER_FAULT -eq 'foreign-baseline') { $document.Baselines.Remove('rac2/item:30'); $document.Baselines['synthetic/item:30']=$samples }
                            $document | ConvertTo-Json -Depth 6 -Compress | Set-Content -LiteralPath $report
                        }
                    } elseif ($ArgumentList -contains '--shots') {
                        if ($ArgumentList -contains '--shots-world') { $marker='reason=shots-missing-gc-source exitCode=2' }
                        else { $marker='reason=shots-list-read-failed exitCode=2' }
                        $code=2
                    } elseif ($ArgumentList -contains '--quit-after') { $marker='[cli-tool-engine-smoke] started' }
                    elseif ($ArgumentList -contains 'res://diagnostics/CliToolEngineSmoke.tscn') { $marker='[cli-tool-engine-smoke] PASS:' }
                    elseif ($ArgumentList -contains '--test-scene') { $marker='[fusion-smoke] PASS:' }
                    elseif ($ArgumentList -contains '--fusion-smoke') { $marker='[fusion-smoke] PASS:' }
                    [IO.File]::WriteAllText($StandardOutputPath,$marker)
                    $errorText=switch($env:OBP_TEST_RUNNER_FAULT){'leak'{'WARNING: 2 ObjectDB instances were leaked at exit.'} 'error'{'ERROR: injected late engine failure'} default{''}}
                    if ($code -ne 0) {
                        switch ($env:OBP_TEST_RUNNER_FAULT) {
                            'negative-rid-out' { $marker += "`nWARNING: 1 RID of type CanvasItem was leaked." }
                            'negative-rids-err' { $errorText = 'WARNING: 2 RIDs of type CanvasItem were leaked.' }
                            'negative-shader-err' { $errorText = 'SHADER ERROR: invalid shader' }
                            'negative-unhandled-out' { $marker += "`nUnhandled exception. System.InvalidOperationException: injected" }
                            'negative-engine-err' { $errorText = 'E 0:00:04:123: engine error' }
                            'negative-managed-out' { $marker += "`nSystem.ArgumentException: invalid sample" }
                        }
                        [IO.File]::WriteAllText($StandardOutputPath,$marker)
                    }
                    [IO.File]::WriteAllText($StandardErrorPath,$errorText)
                    if($ArgumentList -contains '--log-file'){Set-Content -LiteralPath $ArgumentList[[Array]::IndexOf($ArgumentList,'--log-file')+1] ($marker+"`n"+$errorText)}
                    return $code
                }
                '@
                Set-Content (Join-Path $root '.tools/godot/fake.ps1') @'
                $log = $args[[Array]::IndexOf($args,'--log-file')+1]
                $text='[fusion-smoke] PASS:'
                if($env:OBP_TEST_RUNNER_FAULT -eq 'leak'){$text += "`nWARNING: 2 ObjectDB instances were leaked at exit."}
                if($env:OBP_TEST_RUNNER_FAULT -eq 'error'){$text += "`nERROR: injected late engine failure"}
                Set-Content -LiteralPath $log $text
                $global:LASTEXITCODE=0
                '@
                Set-Content (Join-Path $tools 'record-playtest.ps1') @'
                $path=$args[[Array]::IndexOf($args,'-Out')+1]
                $writer=[IO.StreamWriter]::new([IO.Compression.GZipStream]::new([IO.File]::Create($path),[IO.Compression.CompressionLevel]::Fastest))
                try{$writer.WriteLine('{"Header":{"Weapons":{"Sources":{"rac2":"fixture"}}}}')}finally{$writer.Dispose()}
                if($env:OBP_TEST_RUNNER_FAULT -eq 'record-log-error'){Set-Content (Join-Path (Split-Path $path) 'record-native.log') 'ERROR: late recording failure'}
                $global:LASTEXITCODE=0
                '@
                Set-Content (Join-Path $tools 'replay-playtest.ps1') @'
                $path=$args[[Array]::IndexOf($args,'-Report')+1]
                $invalid=$args -contains '-AllowBuildChange'
                if($invalid){$result=@{Result='invalid';Error='Recorded cross-game weapon sources do not match (rac2). Attach the exact original sources used by the recording.'}}
                else{$result=@{Result='matched';Markers=@(1,2,3);CheckpointsCompared=10}}
                $result|ConvertTo-Json|Set-Content -LiteralPath $path
                if($env:OBP_TEST_RUNNER_FAULT -eq 'replay-log-rid'){Set-Content (Join-Path (Split-Path $path) 'replay-native.log') 'WARNING: 1 RID of type CanvasItem was leaked.'}
                $global:LASTEXITCODE=if($invalid){2}else{0}
                '@
                if ({{{{{Quote(fault)}}}}} -eq 'stale') {
                    New-Item -ItemType Directory $out | Out-Null
                    $samples = @(1..40 | ForEach-Object { @{Tick=$_*6;Clock=$_/10.0} })
                    $history = @{Version=1;Baselines=@{'rac2/item:30'=$samples;'rac3/item:43'=$samples}} | ConvertTo-Json -Depth 6 -Compress
                    foreach($fps in 30,120){Set-Content (Join-Path $out "trace-$fps.json") $history}
                }
                $source = Join-Path $root 'source.iso'
                $parameters = @{Out=$out;SkipBuild=$true}
                if ({{{{{Quote(runner)}}}}} -ne 'test-diagnostic-cli.ps1') {
                    $parameters.Rac1Iso=$source;$parameters.Rac2Iso=$source;$parameters.Rac3Iso=$source
                }
                & (Join-Path $tools {{{{{Quote(runner)}}}}}) @parameters
                """;
            var result = await Run(shell, script);
            Assert.True((result.Code == 0) == expected, runner + ": " + fault + "\n" + result.Error + result.Output);
            if (!expected)
            {
                string reason = fault switch
                {
                    "stale" => "Native player report was not produced by this run.",
                    "empty" or "foreign-baseline" => "missing its two weapon baselines",
                    "partial" => "does not contain a complete four-second trial",
                    "record-log-error" or "replay-log-rid" => "Godot lifecycle error",
                    _ when fault.StartsWith("negative-", StringComparison.Ordinal) => "Diagnostic CLI check bad-list failed",
                    _ => runner switch
                    {
                        "player-session-smoke.ps1" => "Player session lifecycle smoke failed",
                        "player-capabilities-smoke.ps1" => "Player capabilities smoke failed",
                        "test-diagnostic-cli.ps1" => "Diagnostic CLI check lab failed",
                        _ => "Fusion fixtures failed",
                    },
                };
                Assert.Contains(reason, result.Error + result.Output);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static IEnumerable<object[]> MovementReplayRunnerCases
    {
        get
        {
            foreach (string shell in new[] { "powershell.exe", "pwsh" })
                foreach (string fault in new[] { "clean", "profile", "nonzero", "no-marker", "leak", "missing-report", "stale",
                    "partial", "bad-tick", "bad-vector", "bad-identity", "foreign-state", "missing-profile", "same-output", "alias-content" })
                    yield return [shell, fault, fault switch { "clean" or "profile" => 0, "nonzero" => 17, "same-output" or "alias-content" => 1, _ => 2 }];
        }
    }

    [SkippableTheory]
    [MemberData(nameof(MovementReplayRunnerCases))]
    public async Task MovementReplayRequiresFreshCompleteFiniteEvidence(string shell, string fault, int expectedExit)
    {
        shell = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp movement runner " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string state = "Synthetic state argument only; never decoded as retail data.";
        try
        {
            string script = $$$$$"""
                $ErrorActionPreference='Stop'
                $root={{{{{Quote(directory)}}}}}
                $tools=Join-Path $root 'tools'
                $engineDir=Join-Path $root '.tools/godot'
                New-Item -ItemType Directory $tools,$engineDir | Out-Null
                foreach($name in 'replay-movement-state.ps1','obp-smoke-process.ps1'){
                    Copy-Item (Join-Path {{{{{Quote(Path.Combine(RepoPaths.Root, "tools"))}}}}} $name) $tools
                }
                Set-Content (Join-Path $tools 'godot-toolchain.json') '{"godot":{"artifacts":{"win64":{"exe":"fake.ps1"}}}}'
                $state=Join-Path $root 'state.json'
                $report=Join-Path $root 'report.json'
                [IO.File]::WriteAllText($state,{{{{{Quote(state)}}}}})
                $env:OBP_TEST_MOVEMENT_FAULT={{{{{Quote(fault)}}}}}
                Set-Content (Join-Path $tools 'fixture-runtime.ps1') @'
                function Invoke-FixtureMovement {
                    param([string[]]$ArgumentList)
                    function Option($name){$index=[Array]::IndexOf($ArgumentList,$name);if($index -lt 0){throw 'Missing fixture argument'};return $ArgumentList[$index+1]}
                    $state=Option '--load-movement-state'
                    $out=Option '--replay-out'
                    if((Option '--replay-forward') -ne '2' -or -not [IO.Path]::IsPathRooted($state) -or -not [IO.Path]::IsPathRooted($out)){throw 'Movement fixture argument boundary changed'}
                    $fault=$env:OBP_TEST_MOVEMENT_FAULT
                    if($fault -in 'profile','missing-profile' -and $ArgumentList -notcontains '--replay-profile'){throw 'Missing requested profiling argument'}
                    $samples=@(0..2 | ForEach-Object { @{Tick=$_;Position=@{X=$_;Y=0;Z=0};Velocity=@{X=1;Y=0;Z=0};FacingYaw=0;ControlHeading=0} })
                    $report=@{StateFile=[IO.Path]::GetFileName($state);HostAssemblyId='00000000-0000-0000-0000-000000000001';PhysicsHz=60;Samples=$samples}
                    switch($fault){
                        'profile' {foreach($sample in $samples){$profile=@{};foreach($field in 'ElapsedMs','RenderFrame','ControllerMs','ContactMs','SlideMs','SmallStepMs','CrestMs','PresentationMs','CameraMs','EngineProcessMs','EnginePhysicsMs','RenderCpuMs','RenderGpuMs','Fps'){$profile[$field]=0};$sample.Profile=$profile}}
                        'partial' {$report.Samples=@($samples[0])}
                        'bad-tick' {$samples[2].Tick=99}
                        'bad-vector' {$samples[1].Position.X='NaN'}
                        'bad-identity' {$report.HostAssemblyId=''}
                        'foreign-state' {$report.StateFile='another-state.json'}
                    }
                    if($fault -notin 'missing-report','stale'){$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $out}
                    $output=if($fault -eq 'no-marker'){''}else{'[movement-replay] PASS; frames=2, output=fixture'}
                    $errorText=if($fault -eq 'leak'){'WARNING: 1 RID of type CanvasItem was leaked.'}else{''}
                    return [pscustomobject]@{Code=$(if($fault -eq 'nonzero'){17}else{0});Output=$output;Error=$errorText}
                }
                '@
                Set-Content (Join-Path $engineDir 'fake.ps1') @'
                . (Join-Path $PSScriptRoot '../../tools/fixture-runtime.ps1')
                $result=Invoke-FixtureMovement -ArgumentList $args
                Write-Output $result.Output
                if($result.Error){[Console]::Error.WriteLine($result.Error)}
                $global:LASTEXITCODE=$result.Code
                '@
                Set-Content (Join-Path $tools 'obp-native-process.ps1') @'
                . (Join-Path $PSScriptRoot 'fixture-runtime.ps1')
                function Invoke-ObpNativeProcessCapture {
                    param($FilePath,[string[]]$ArgumentList,$WorkingDirectory,$StandardOutputPath,$StandardErrorPath,$OnOutput)
                    $result=Invoke-FixtureMovement -ArgumentList $ArgumentList
                    [IO.File]::WriteAllText($StandardOutputPath,$result.Output)
                    [IO.File]::WriteAllText($StandardErrorPath,$result.Error)
                    if($OnOutput){& $OnOutput $result.Output}
                    return $result.Code
                }
                '@
                if({{{{{Quote(fault)}}}}} -eq 'stale'){
                    . (Join-Path $tools 'fixture-runtime.ps1')
                    $env:OBP_TEST_MOVEMENT_FAULT='clean'
                    $null=Invoke-FixtureMovement -ArgumentList @('--load-movement-state',$state,'--replay-forward','2','--replay-out',$report)
                    $env:OBP_TEST_MOVEMENT_FAULT='stale'
                }
                if({{{{{Quote(fault)}}}}} -eq 'same-output'){$report=$state}
                if({{{{{Quote(fault)}}}}} -eq 'alias-content'){Copy-Item -LiteralPath $state -Destination $report}
                $parameters=@{State=$state;Frames=2;Out=$report;SkipBuild=$true;Rac1Iso='synthetic.iso'}
                if({{{{{Quote(fault)}}}}} -in 'profile','missing-profile'){$parameters.CollectTimings=$true}
                & (Join-Path $tools 'replay-movement-state.ps1') @parameters
                exit $LASTEXITCODE
                """;
            var result = await Run(shell, script);
            Assert.True(result.Code == expectedExit, fault + ": expected " + expectedExit + ", got " + result.Code + "\n" + result.Error + result.Output);
            Assert.Equal(state, File.ReadAllText(Path.Combine(directory, "state.json")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string RequireShell(string name)
    {
        Skip.If(name == "powershell.exe" && !OperatingSystem.IsWindows(), "Windows PowerShell compatibility case.");
        string executable = name == "pwsh" && OperatingSystem.IsWindows() ? "pwsh.exe" : name;
        string? path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, executable)).FirstOrDefault(File.Exists);
        Skip.If(path is null, executable + " is not installed.");
        return path!;
    }

    private static async Task<(int Code, string Output, string Error)> Run(string shell, string script, Action<string>? observe = null)
    {
        if (Path.GetFileName(shell) is not ("pwsh" or "pwsh.exe" or "powershell.exe") || !File.Exists(shell))
            throw new ArgumentException("An installed PowerShell executable is required.", nameof(shell));
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start the PowerShell fixture.");
        var output = observe is null ? process.StandardOutput.ReadToEndAsync() : Observe(process.StandardOutput);
        var error = observe is null ? process.StandardError.ReadToEndAsync() : Observe(process.StandardError);
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);

        async Task<string> Observe(StreamReader reader)
        {
            var text = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line)
            {
                text.AppendLine(line);
                observe!(line);
            }
            return text.ToString();
        }
    }
}
