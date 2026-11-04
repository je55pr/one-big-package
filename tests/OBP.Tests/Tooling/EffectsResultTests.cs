using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OBP.Tests;

public sealed class EffectsResultTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string shell in new[] { "powershell", "pwsh" })
            foreach (string mode in new[] { "render", "record", "replay" })
            {
                foreach (string fault in new[] { "clean", "incomplete", "missing", "late-error", "leak", "managed-exception", "tagged-failure", "rid-leak", "rids-leak" })
                    yield return [shell, mode, fault];
                foreach (string fault in mode == "render" ? new[] { "empty-captures", "missing-image", "missing-family", "invalid-image", "truncated-image", "wrong-dimensions" }
                    : new[] { "short-tape", "missing-end", "missing-commands", "wrong-count" })
                    yield return [shell, mode, fault];
                if (mode == "replay") yield return [shell, mode, "invalid-report"];
            }
    }

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task EachModeRequiresItsCompleteOutcome(string shell, string mode, string fault)
    {
        string executable = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp effects result " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Protocol observations only; these fixtures do not claim rendered output or native parity.
            var captures = new[] { "fusion-lab.png", "rac1-LEVEL0.png", "rac2-LEVEL1.png", "rac3-TABLE1.png", "obp-fusion-lab.png" }
                .Select(name => new { File = name, Width = fault == "wrong-dimensions" ? 2 : 1, Height = 1, ChangedPixels = new { rac1 = 100, rac2 = fault == "missing-family" ? 0 : 100, rac3 = 100 } }).ToArray();
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            foreach (var capture in captures) File.WriteAllBytes(Path.Combine(directory, capture.File), png);
            if (fault == "invalid-image") File.WriteAllText(Path.Combine(directory, captures[0].File), "synthetic text is not a PNG capture");
            if (fault == "truncated-image") File.WriteAllBytes(Path.Combine(directory, captures[0].File), png[..8]);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
            {
                Version = 1,
                Passed = true,
                Captures = fault == "empty-captures" ? [] : captures,
                Samples = new int[14],
            }));
            int frames = fault == "short-tape" ? 65 : 66, steps = frames * 2;
            using (var file = File.Create(Path.Combine(directory, "effects.jsonl.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip))
            {
                writer.WriteLine("""{"Header":{"Version":2,"Destination":"obp:fusion-lab"}}""");
                for (int index = 0; index < steps; index++)
                {
                    int frame = index / 2;
                    object[] commands = index % 2 == 1 && frame is 1 or 30 or 40 && fault != "missing-commands"
                        ? [new { Kind = "effects", Value = frame == 30 ? "reset" : "start" }] : [];
                    writer.WriteLine(JsonSerializer.Serialize(new
                    {
                        Step = new
                        {
                            Index = index,
                            Kind = index % 2,
                            Checkpoint = index == steps - 1 ? new { Synthetic = true } : null,
                            Commands = commands
                        }
                    }));
                }
                if (fault != "missing-end") writer.WriteLine(JsonSerializer.Serialize(new { End = new { Steps = steps, Reason = "saved" } }));
            }
            File.WriteAllText(Path.Combine(directory, "replay.json"), JsonSerializer.Serialize(new
            {
                Result = fault == "invalid-report" ? "invalid" : "matched",
                Error = (string?)null,
                FirstDifference = (string?)null,
                Steps = fault == "wrong-count" ? steps - 1 : steps,
                CheckpointsCompared = 2,
            }));
            if (fault == "missing") File.Delete(Path.Combine(directory, mode == "render" ? "report.json" : mode == "record" ? "effects.jsonl.gz" : "replay.json"));
            if (fault == "missing-image") File.Delete(Path.Combine(directory, captures[0].File));
            string log = mode == "render" ? "[effects-smoke] PASS:" : mode == "record"
                ? $"[playtest] SAVED synthetic; steps={(fault == "wrong-count" ? steps - 1 : steps)}; markers=0\nreason=playtest-record-smoke-pass exitCode=0"
                : "[playtest] PASS;\nreason=playtest-replay-complete exitCode=0";
            if (fault == "incomplete") log = "[effects-smoke] started";
            if (fault == "late-error") log += "\n[playtest] finalization failed: injected";
            if (fault == "leak") log += "\nWARNING: 2 ObjectDB instances were leaked at exit.";
            if (fault == "managed-exception") log += "\nSystem.InvalidOperationException: injected";
            if (fault == "tagged-failure") log += "\n[effects-smoke] FAIL: injected";
            if (fault == "rid-leak") log += "\nWARNING: 1 RID of type CanvasItem was leaked.";
            if (fault == "rids-leak") log += "\nWARNING: 2 RIDs of type CanvasItem were leaked.";
            string script = $"""
                $ErrorActionPreference = 'Stop'
                . {Quote(Path.Combine(RepoPaths.Root, "tools/obp-effects-result.ps1"))}
                Assert-ObpEffectsResult -Mode {Quote(mode)} -Directory {Quote(directory)} -Log {Quote(log)} -StopFrame 65
                """;
            var result = await Run(executable, script);
            Assert.True(fault == "clean" ? result.Code == 0 : result.Code != 0, fault + ": " + result.Output);
            if (fault != "clean") Assert.Contains(ExpectedFailure(mode, fault), result.Output, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string ExpectedFailure(string mode, string fault) => fault switch
    {
        "late-error" or "leak" or "managed-exception" or "tagged-failure" or "rid-leak" or "rids-leak" => "emitted a failure or leaked resources",
        "incomplete" => $"Effects {(mode == "record" ? "recording" : mode)} did not complete",
        "missing" when mode == "record" => "Effects recording is missing or empty",
        "missing" => "Cannot find path",
        "empty-captures" => "Effects render report is incomplete",
        "missing-image" or "missing-family" => "Effects render lacks its captured trilogy images",
        "invalid-image" or "truncated-image" or "wrong-dimensions" => "Effects render lacks a valid PNG header",
        "short-tape" or "missing-end" or "missing-commands" => "Effects tape did not complete its requested native windows",
        "wrong-count" when mode == "record" => "Effects recording did not complete",
        "wrong-count" or "invalid-report" => "Effects replay report is incomplete or diverged",
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    [SkippableTheory]
    [InlineData("powershell", true)]
    [InlineData("powershell", false)]
    [InlineData("pwsh", true)]
    [InlineData("pwsh", false)]
    public async Task RunnerProtectsRecordingsAndRemovesStaleReportsBeforeNativeLaunch(string shell, bool record)
    {
        string executable = RequireShell(shell);
        string directory = Path.Combine(Path.GetTempPath(), "obp effects launch " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Deliberately unlaunchable; these cases check the guards before native execution.
            string placeholder = Path.Combine(directory, "native-placeholder.exe");
            File.WriteAllText(placeholder, "synthetic non-executable source/engine placeholder");
            string tape = Path.Combine(directory, "effects.jsonl.gz"), report = Path.Combine(directory, "replay.json");
            File.WriteAllText(tape, "protected existing synthetic tape");
            File.WriteAllText(report, "stale synthetic matched report");
            var result = await Run(executable, $"""
                $ErrorActionPreference = 'Stop'
                & {Quote(Path.Combine(RepoPaths.Root, "tools/test-effects.ps1"))} -SkipBuild -Mode {(record ? "record" : "replay")} `
                    -Godot {Quote(placeholder)} -Rac1Iso {Quote(placeholder)} -Rac2Iso {Quote(placeholder)} -Rac3Iso {Quote(placeholder)} `
                    -OutputDirectory {Quote(directory)}
                """);
            Assert.True(result.Code != 0, result.Output);
            Assert.Equal("protected existing synthetic tape", File.ReadAllText(tape));
            if (record)
            {
                Assert.Contains("Choose a new OutputDirectory for a fresh recording", result.Output, StringComparison.Ordinal);
                Assert.Equal("stale synthetic matched report", File.ReadAllText(report));
            }
            else
            {
                Assert.Contains("Start", result.Output, StringComparison.Ordinal);
                Assert.False(File.Exists(report));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string RequireShell(string name)
    {
        string file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        string? executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path, file)).FirstOrDefault(File.Exists);
        Skip.If(executable is null, file + " is not installed.");
        return executable!;
    }

    private static async Task<(int Code, string Output)> Run(string executable, string script)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stderr + await stdout);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
