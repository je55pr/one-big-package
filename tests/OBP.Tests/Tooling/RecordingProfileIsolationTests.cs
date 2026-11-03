using System.Diagnostics;

namespace OBP.Tests;

public sealed class RecordingProfileIsolationTests
{
    [SkippableTheory]
    [InlineData(true, 0, 0)]
    [InlineData(true, 17, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 0, 2)]
    [InlineData(true, 0, 3)]
    [InlineData(false, 0, 0)]
    public async Task SmokeProfilesUseOwnedTempCopiesForExternalOutputsAndAlwaysCleanThem(bool smoke, int exitCode, int profileMode)
    {
        string shellName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        string? shell = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, shellName)).FirstOrDefault(File.Exists);
        Skip.If(shell is null, "PowerShell is not installed.");
        string directory = Path.Combine(Path.GetTempPath(), "obp recording wrapper " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string tools = Path.Combine(directory, "tools"), engine = Path.Combine(directory, ".tools", "godot"),
                approved = Path.Combine(directory, "approved temp"), external = Path.Combine(directory, "external output");
            foreach (string path in new[] { tools, engine, approved, external }) Directory.CreateDirectory(path);
            string wrapper = Path.Combine(tools, "record-playtest.ps1"), original = Path.Combine(directory, "original.profile.json"),
                output = Path.Combine(external, "capture.run.jsonl.gz");
            File.Copy(Path.Combine(RepoPaths.Root, "tools", "record-playtest.ps1"), wrapper);
            File.WriteAllText(Path.Combine(tools, "obp-window.ps1"), "function Get-ObpWindowArguments { return @() }");
            File.WriteAllText(Path.Combine(tools, "godot-toolchain.json"), """{"godot":{"artifacts":{"win64":{"exe":"fake-engine.ps1"}}}}""");
            if (profileMode == 0) File.WriteAllText(original, "original primary");
            if (profileMode is 0 or 1) File.WriteAllText(original + ".bak", "original backup");
            File.WriteAllText(Path.Combine(engine, "fake-engine.ps1"), """
                $ErrorActionPreference = 'Stop'
                function Argument([string]$name) {
                    $index = [Array]::IndexOf($script:args, $name)
                    if ($index -lt 0) { return $null }
                    return $script:args[$index + 1]
                }
                $target = Argument '--record-playtest'
                $profile = Argument '--profile'
                if ($env:OBP_PROFILE_MODE -eq '3') {
                    if ($script:args -notcontains '--no-profile' -or $profile) { throw 'Expected no shared profile.' }
                    [IO.File]::WriteAllText($target, 'no-profile')
                }
                else {
                    if ($env:OBP_PROFILE_SMOKE -eq 'True') {
                        if (-not $profile.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::Ordinal)) { throw 'Profile is outside approved temp.' }
                        if ($profile -eq $env:OBP_PROFILE_ORIGINAL) { throw 'Shared profile was passed to smoke.' }
                    }
                    elseif ($profile -ne $env:OBP_PROFILE_ORIGINAL) { throw 'Human profile semantics changed.' }
                    if ($env:OBP_PROFILE_MODE -eq '0' -and [IO.File]::ReadAllText($profile) -ne 'original primary') { throw 'Primary copy changed.' }
                    if ($env:OBP_PROFILE_MODE -in @('0','1') -and [IO.File]::ReadAllText($profile + '.bak') -ne 'original backup') { throw 'Backup copy changed.' }
                    [IO.File]::WriteAllText($target, $profile)
                    if ($env:OBP_PROFILE_SMOKE -eq 'True') { [IO.File]::WriteAllText($profile, 'mutated isolated state') }
                }
                exit ([int]$env:OBP_PROFILE_EXIT)
                """);
            var start = new ProcessStartInfo(shell!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper, "-SkipBuild", "-Out", output }) start.ArgumentList.Add(argument);
            if (smoke) { start.ArgumentList.Add("-Smoke"); start.ArgumentList.Add("-Headless"); }
            if (profileMode != 3) { start.ArgumentList.Add("-Profile"); start.ArgumentList.Add(original); }
            start.Environment["TEMP"] = start.Environment["TMP"] = start.Environment["TMPDIR"] = approved;
            start.Environment["OBP_PROFILE_MODE"] = profileMode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["OBP_PROFILE_SMOKE"] = smoke.ToString();
            start.Environment["OBP_PROFILE_EXIT"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["OBP_PROFILE_ORIGINAL"] = original;
            foreach (string name in new[] { "OBP_RAC1_ISO", "OBP_GC_ISO", "OBP_UYA_ISO" }) start.Environment.Remove(name);
            using var process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == exitCode, await stderr + await stdout);
            Assert.True(File.Exists(output));
            Assert.Empty(Directory.GetFileSystemEntries(approved));
            if (profileMode == 0) Assert.Equal("original primary", File.ReadAllText(original));
            if (profileMode is 0 or 1) Assert.Equal("original backup", File.ReadAllText(original + ".bak"));
            if (smoke && profileMode != 3) Assert.False(Directory.Exists(Path.GetDirectoryName(File.ReadAllText(output))));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
