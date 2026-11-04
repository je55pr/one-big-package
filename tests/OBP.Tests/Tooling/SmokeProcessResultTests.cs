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
                        [IO.File]::WriteAllText($StandardOutputPath, "The audio fixture's player is unavailable. reason=rac1-player-audio-smoke-fail exitCode=1")
                        [IO.File]::WriteAllText($StandardErrorPath, '')
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
            var arguments = File.ReadAllLines(calls).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray();
            string[] route = destinations.Split(',');
            Assert.Equal(expected == 0 ? route.Length : 1, arguments.Length);
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
