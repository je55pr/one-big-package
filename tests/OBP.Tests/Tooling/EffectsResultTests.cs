using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OBP.Tests;

public sealed class EffectsResultTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string shell in new[] { "powershell.exe", "pwsh.exe" })
            foreach (string mode in new[] { "render", "record", "replay" })
            {
                foreach (string fault in new[] { "clean", "incomplete", "missing", "late-error", "leak", "managed-exception", "tagged-failure" })
                    yield return [shell, mode, fault];
                foreach (string fault in mode == "render" ? new[] { "empty-captures", "missing-image", "missing-family" }
                    : new[] { "short-tape", "missing-end", "missing-commands", "wrong-count" })
                    yield return [shell, mode, fault];
                if (mode == "replay") yield return [shell, mode, "invalid-report"];
            }
    }

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public async Task EachModeRequiresItsCompleteOutcome(string shell, string mode, string fault)
    {
        string? executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path, shell)).FirstOrDefault(File.Exists);
        Skip.If(executable is null, shell + " is not installed.");
        string directory = Path.Combine(Path.GetTempPath(), "obp effects result " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Protocol observations only; these fixtures do not claim rendered output or native parity.
            var captures = new[] { "fusion-lab.png", "rac1-LEVEL0.png", "rac2-LEVEL1.png", "rac3-TABLE1.png", "obp-fusion-lab.png" }
                .Select(name => new { File = name, Width = 960, Height = 540, ChangedPixels = new { rac1 = 100, rac2 = fault == "missing-family" ? 0 : 100, rac3 = 100 } }).ToArray();
            foreach (var capture in captures) File.WriteAllText(Path.Combine(directory, capture.File), "synthetic image observation");
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
            string script = $"""
                $ErrorActionPreference = 'Stop'
                . {Quote(Path.Combine(RepoPaths.Root, "tools/obp-effects-result.ps1"))}
                Assert-ObpEffectsResult -Mode {Quote(mode)} -Directory {Quote(directory)} -Log {Quote(log)} -StopFrame 65
                """;
            var start = new ProcessStartInfo(executable!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(fault == "clean" ? process.ExitCode == 0 : process.ExitCode != 0, fault + ": " + await stderr + await stdout);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
