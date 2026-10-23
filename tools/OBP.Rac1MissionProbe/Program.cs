using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.IO;
using OBP.PS2;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;

// Original retail payloads and the optional native interpreter executable stay
// under ignored captures/. Only scalar observations and code hashes are emitted.
var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException("Expected --iso, --reference-root, --cxx and --out.");
    options.Add(args[i], args[i + 1]);
}
string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "OneBigPackage.sln")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run inside the OBP checkout.");
string work = Path.Combine(root, "captures", "mission-native-oracle");
Directory.CreateDirectory(work);
using var disc = new FileRandomAccessReader(Path.GetFullPath(options["--iso"]));
var boot = Ps2Boot.OpenDisc(disc);
byte[] executable = boot.BootExecutable.Read(0, checked((int)boot.BootExecutable.Length));
string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
if (Hash(executable) != "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9")
    throw new InvalidDataException("Unsupported retail boot executable.");
var catalogue = Rac1DiscIndex.Read(disc);
var overlays = new Dictionary<int, Rac1ExecutableOverlay>();
var programs = new List<object>();
var infobots = new List<object>();
foreach (var level in catalogue.Levels)
{
    var overlay = Rac1ExecutableOverlay.Read(disc, level);
    var instances = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances;
    foreach (var actor in instances.Where(m => m.OClass == 750))
    {
        var authored = Rac1InfobotAuthored.Read(actor);
        infobots.Add(new { level = level.LevelId, actor.Index, authored });
    }
    if (level.LevelId is not (0 or 1)) continue;
    overlays.Add(level.LevelId, overlay);
    var ram = new byte[32 * 1024 * 1024];
    foreach (var segment in Ps2Boot.ReadBootProgramHeaders(boot).Where(s => s.Type == 1 && s.FileSize > 0))
        executable.AsSpan(checked((int)segment.Offset), checked((int)segment.FileSize))
            .CopyTo(ram.AsSpan(checked((int)segment.VirtualAddress), checked((int)segment.FileSize)));
    overlay.CopyLoadedSections(ram);
    File.WriteAllBytes(Path.Combine(work, $"level{level.LevelId}.bin"), ram);
    foreach (var actor in instances.Where(m => m.OClass == 750 || level.LevelId == 0 && m.OClass == 834 || level.LevelId == 1 && m.OClass == 774))
    {
        File.WriteAllBytes(Path.Combine(work, $"pvar-{level.LevelId}-{actor.OClass}.bin"), actor.PVar!);
        if (actor.OClass == 750) continue;
        var program = Rac1TalkProgram.Read(overlay, level.LevelId, actor)!;
        programs.Add(new
        {
            level = level.LevelId,
            actor.OClass,
            actor.Index,
            program.GlobalTalkerIndex,
            records = program.Records.Values.OrderBy(r => r.Index),
            prices = program.BoltPrices,
        });
    }
}
File.Copy(Path.Combine(work, "pvar-1-750.bin"), Path.Combine(work, "pvar-0-750.bin"), overwrite: true);
var blocks = new (int Level, string Name, int Start, int End)[]
{
    (0, "infobotUpdate", 0x2d5de8, 0x2d6bf0), (0, "infobotDispatch", 0x1e9f60, 0x1e9f90),
    (0, "talkUpdateInitStep", 0x266448, 0x266d48), (0, "VeldinActor", 0x2d9dc8, 0x2d9f78),
    (0, "destinationAdmission", 0x2607d0, 0x260860), (0, "missionExit", 0x28eeb0, 0x28eee0),
    (0, "talkerRegistration", 0x242c14, 0x242c34), (0, "lateLoaderCommit", 0x2933c4, 0x29344c),
    (0, "entryAdmission", 0x23d160, 0x23d178), (1, "PlumberProgression", 0x2ff118, 0x2ff3d0),
    (1, "talkUpdateInitStep", 0x27b028, 0x27b930), (1, "destinationAdmission", 0x2756d0, 0x275760),
};
var code = blocks.Select(b => new
{
    level = b.Level,
    name = b.Name,
    start = $"0x{b.Start:x8}",
    end = $"0x{b.End:x8}",
    sha256 = Hash(overlays[b.Level].Read(b.Start, b.End - b.Start)),
}).ToArray();

string reference = Path.GetFullPath(options["--reference-root"]);
const string referenceRevision = "3fd14c12e4dff2fd4b6661a6e54187faea3f76c8";
if (Run("git", ["-C", reference, "rev-parse", "HEAD"], null).Trim() != referenceRevision)
    throw new InvalidDataException("Mission oracle requires the acknowledged rac1-native-game reference revision.");
string compiler = Path.GetFullPath(options["--cxx"]);
string oracle = Path.Combine(work, OperatingSystem.IsWindows() ? "mission-oracle.exe" : "mission-oracle");
var compile = new List<string>();
if (Path.GetFileNameWithoutExtension(compiler).Equals("zig", StringComparison.OrdinalIgnoreCase)) compile.Add("c++");
compile.AddRange(["-std=c++20", "-O2", "-ffp-contract=off", "-Wno-nullability-completeness", "-I", reference,
    Path.Combine(root, "tools/rac1-mission-native-oracle.cpp"),
    Path.Combine(reference, "runtime/ee/ee_interpreter.cpp"), Path.Combine(reference, "runtime/vu/vu_interpreter.cpp"), "-o", oracle]);
_ = Run(compiler, compile, null);
var trials = new List<object>();
void Trial(string name, string mode, int level, int cls, string[] inputs)
{
    string output = Run(oracle, [Path.Combine(work, $"level{level}.bin"), Path.Combine(work, $"pvar-{level}-{cls}.bin"), mode], string.Join('\n', inputs) + "\n");
    var samples = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
    {
        int[] row = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (row.Length != 10) throw new InvalidDataException("Incomplete original-code mission trace.");
        return new { state = row[0], talkRecord = row[1], lastPlayed = row[2], sceneKind = row[3], sceneId = row[4], pendingTarget = row[5], transitionActive = row[6], admittedCount = row[7], bolts = row[8], retireCalls = row[9] };
    }).ToArray();
    if (samples.Length != inputs.Length) throw new InvalidDataException("Missing original-code mission trace samples.");
    trials.Add(new { name, mode, level, nativeClassId = cls, inputs, samples });
}
Trial("Veldin-actor-to-Novalis", "ship", 0, 834,
[
    "tick 20 0 0 0 4 0 0 0 0", "tick 20 0 0 0 4 0 0 -1 0",
    "tick 6.01 0 0 0 4 0 0 -1 0", "tick 5.9 0 0 0 0 0 0 -1 0",
    "tick 5.9 0 0 0 4 29 0 -1 0", "tick 5.9 0 0 0 4 0 0 -1 0",
    "complete 5.9 0 0 0 4 0 0 -1 0", "complete 5.9 0 0 0 4 0 0 -1 0",
    "complete 5.9 0 0 0 4 0 0 -1 0", "complete 5.9 0 0 0 4 0 0 -1 0",
    "tick 5.9 0 0 0 4 0 0 -1 0", "tick 5.9 0 0 0 4 0 0 -1 0",
    "entry 5.9 0 0 0 4 0 0 -1 0", "entry 5.9 0 0 0 4 0 0 -1 0",
]);
Trial("Infobot-active-pickup-and-admission", "infobot", 0, 750,
[
    "tick 2 0 0 0 4 0 0 0 0", "tick 1 0 0 0 4 0 0 -1 0",
    "tick .999 0 0 0 0 0 0 -1 0", "tick .999 0 0 0 4 0 0 -1 0",
    "tick .999 0 0 0 4 0 0 -1 0", "tick .999 0 0 0 4 0 0 -1 0",
    "tick .999 0 0 0 4 0 0 -1 0", "tick .999 0 0 0 4 0 0 -1 0",
]);
// Veldin has no placed class-750 PVar: use the source Novalis template for
// the controlled unlinked active variant, with the explicit oracle overrides.
Trial("Plumber-price-gate-and-Aridia", "plumber", 1, 774,
[
    "tick 20 0 0 3.14159265 4 0 0 499 0", "tick 9.9 0 0 3.14159265 4 0 0 -1 0",
    "complete 9.9 0 0 3.14159265 4 0 0 -1 0", "tick 9.9 0 0 3.14159265 4 0 0 -1 0",
    "tick 9.9 0 0 3.14159265 4 0 1 -1 0", "tick 9.9 0 0 3.14159265 4 0 0 -1 0",
    "tick 9.9 0 0 3.14159265 4 0 1 500 0", "complete 9.9 0 0 3.14159265 4 0 0 -1 0",
    "complete 9.9 0 0 3.14159265 4 0 0 -1 0", "complete 9.9 0 0 3.14159265 4 0 0 -1 0",
    "tick 9.9 0 0 3.14159265 4 0 0 -1 0", "tick 9.9 0 0 3.14159265 4 0 0 -1 0",
]);
var report = new
{
    schema = 1,
    authority = "SCUS-97199 original NTSC-U",
    payloadFree = true,
    referenceRevision,
    executableSha256 = Hash(executable),
    overlays = overlays.Select(p => new { level = p.Key, sha256 = p.Value.Sha256 }),
    programs,
    infobots,
    code,
    trials,
    limits = new[]
    {
        "Original MIPS actor, talk and admission routines; controlled player facts and post-intro Veldin talker state.",
        "Scene/movie completion is supplied at the native talk-step callback boundary; streaming, text, draw, checkpoint and file IO are stubbed.",
        "Infobot trial isolates an active unlinked variant with destination 4 and no scenes; it does not invent a producer for dormant/path-linked actors.",
        "The entry operation replays the separately pinned late CurrentLevel/init-admission boundary, not the disc loader.",
    },
};
string outputPath = Path.GetFullPath(options["--out"]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).ReplaceLineEndings("\n") + "\n");
Console.WriteLine($"Original mission replay: {trials.Count} actor scenarios; {programs.Count} source talk programs; {infobots.Count} placed Infobots.");

string Run(string file, IEnumerable<string> arguments, string? input)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start native mission oracle.");
    if (input is not null) process.StandardInput.Write(input);
    process.StandardInput.Close();
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(180_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Native mission oracle timed out."); }
    Task.WaitAll(stdout, stderr);
    if (process.ExitCode != 0) throw new InvalidOperationException($"Native mission oracle failed: {stderr.Result}");
    return stdout.Result;
}
