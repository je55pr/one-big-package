using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OBP.IO;
using OBP.PS2;
using OBP.RAC1.Level;

var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 == args.Length) throw new ArgumentException("Expected --iso, --reference-root, --cxx, --out.");
    options.Add(args[i], args[i + 1]);
}
string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "OneBigPackage.sln"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run inside the checkout.");
string work = Path.Combine(root, "captures/novalis-route-native-oracle");
Directory.CreateDirectory(work);
using var disc = new FileRandomAccessReader(options["--iso"]);
var boot = Ps2Boot.OpenDisc(disc);
byte[] executable = boot.BootExecutable.Read(0, checked((int)boot.BootExecutable.Length));
string Hash(ReadOnlySpan<byte> b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
if (Hash(executable) != "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9") throw new InvalidDataException("Unsupported source executable.");
var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 1);
var overlay = Rac1ExecutableOverlay.Read(disc, level);
byte[] gameplay = Rac1LevelSettings.ReadGameplay(disc, level);
var instances = Rac1Instances.Parse(gameplay).MobyInstances;
var volumes = Rac1ClipVolumes.Read(gameplay);
byte[] ram = new byte[32 * 1024 * 1024];
foreach (var s in Ps2Boot.ReadBootProgramHeaders(boot).Where(s => s.Type == 1 && s.FileSize > 0))
    executable.AsSpan((int)s.Offset, (int)s.FileSize).CopyTo(ram.AsSpan((int)s.VirtualAddress));
overlay.CopyLoadedSections(ram);
string image = Path.Combine(work, "level1.bin"), boxes = Path.Combine(work, "boxes.bin");
File.WriteAllBytes(image, ram);
int boxAt = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(0x60));
File.WriteAllBytes(boxes, gameplay.AsSpan(boxAt + 16, volumes.Count * 128).ToArray());
string reference = Path.GetFullPath(options["--reference-root"]);
const string revision = "3fd14c12e4dff2fd4b6661a6e54187faea3f76c8";
if (Run("git", ["-C", reference, "rev-parse", "HEAD"]).Trim() != revision) throw new InvalidDataException("Unsupported interpreter reference revision.");
string compiler = Path.GetFullPath(options["--cxx"]), oracle = Path.Combine(work, OperatingSystem.IsWindows() ? "oracle.exe" : "oracle");
var compile = new List<string>();
if (Path.GetFileNameWithoutExtension(compiler).Equals("zig", StringComparison.OrdinalIgnoreCase)) compile.Add("c++");
compile.AddRange(["-std=c++20", "-O2", "-ffp-contract=off", "-Wno-nullability-completeness", "-I", reference,
    Path.Combine(root, "tools/rac1-novalis-route-native-oracle.cpp"), Path.Combine(reference, "runtime/ee/ee_interpreter.cpp"),
    Path.Combine(reference, "runtime/vu/vu_interpreter.cpp"), "-o", oracle]);
Run(compiler, compile);
float[] V(float x, float y, float z) => [x, y, z];
string Floats(IEnumerable<float> values) => string.Join(' ', values.Select(f => f.ToString("R", CultureInfo.InvariantCulture)));
var trials = new List<object>();
foreach (var (m, restored) in instances.Where(m => m.OClass is 703 or 715 or 768 or 769 or 701)
    .SelectMany(m => m.Index == 711 ? new[] { (m, false), (m, true) } : new[] { (m, false) }))
{
    byte[] p = m.PVar!;
    int I(int o) => BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o));
    float F(int o) => BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(o));
    var segments = new List<(int Ticks, float[] Player, float[] Camera, int Run)>();
    float[] home = V(m.Position.X, m.Position.Y, m.Position.Z), far = V(0, 0, 0);
    bool door = m.OClass is 768 or 769, rock = m.OClass == 701;
    if (door)
    {
        float x = m.Position.X, y = m.Position.Y, z = m.Position.Z;
        segments.Add((1, far, far, 0));
        segments.Add((1, V(x + 5, y, z), far, 0));
        segments.Add((1, V(x, y, z + 2), far, 0));
        segments.Add((24, home, far, 0));
        segments.Add((6, far, home, 0));
        segments.Add((5, far, far, 0));
        segments.Add((24, home, far, 0));
        segments.Add((25, far, far, 0));
    }
    else if (rock)
    {
        var t = volumes[I(0x80)].Centre;
        float[] trigger = V(t.X, t.Y, t.Z);
        segments.Add((2, V(t.X, t.Y, t.Z - .01f), far, 0));
        segments.Add((1, V(t.X + 10, t.Y, t.Z), far, 0));
        segments.Add((1, trigger, far, 2));
        segments.Add((210, trigger, far, 0));
    }
    else segments.Add((650, far, far, 0));
    string pvar = Path.Combine(work, $"pvar-{m.OClass}-{m.Index}.bin");
    File.WriteAllBytes(pvar, p);
    var inputs = segments.SelectMany(s => Enumerable.Repeat(Floats(s.Player.Concat(s.Camera)) + " " + s.Run, s.Ticks)).ToArray();
    string stdin = Floats(home.Append(m.Rotation.Z)) + "\n" + string.Join('\n', inputs) + "\n";
    var oracleArgs = new List<string> { image, pvar, boxes, m.OClass.ToString(CultureInfo.InvariantCulture) };
    if (restored) oracleArgs.Add("restored");
    string[] rows = Run(oracle, oracleArgs, stdin).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    if (rows.Length != inputs.Length) throw new InvalidDataException("Incomplete original-code trace.");
    var samples = new List<object>();
    int previousState = -1, previousEnd = -1;
    for (int tick = 0; tick < rows.Length; tick++)
    {
        double[] r = rows[tick].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (r.Length != 8) throw new InvalidDataException("Bad oracle row.");
        if (tick < 6 || tick % 30 == 0 || tick == rows.Length - 1 || r[0] != previousState || r[1] != previousEnd || r[7] != -1)
            samples.Add(new { tick, state = (int)r[0], end = (int)r[1], position = r[2..5], scalar = r[5], timer = (int)r[6], sound = (int)r[7] });
        previousState = (int)r[0]; previousEnd = (int)r[1];
    }
    trials.Add(new
    {
        nativeClassId = m.OClass,
        instance = m.Index,
        restoredFallen = restored,
        home,
        yaw = m.Rotation.Z,
        reach = door ? F(0x14) : 0,
        initialEnd = !door && !rock ? p[0xa4] : 0,
        height = !door && !rock ? F(0xa8) : 0,
        seconds = !door && !rock ? F(0xb0) : 0,
        delay = rock ? BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(0x88)) : 0,
        trigger = rock ? V(volumes[I(0x80)].Centre.X, volumes[I(0x80)].Centre.Y, volumes[I(0x80)].Centre.Z) : far,
        segments = segments.Select(s => new { ticks = s.Ticks, player = s.Player, camera = s.Camera, run = s.Run }),
        samples,
    });
}
var blocks = new[] { ("approachDoors", 0x2fed68, 0x2ff028), ("pistons", 0x2f95c0, 0x2f9810), ("staircaseRocks", 0x2f9080, 0x2f95c0) };
var report = new
{
    schema = 1,
    authority = "SCUS-97199 original NTSC-U",
    payloadFree = true,
    referenceRevision = revision,
    executableSha256 = Hash(executable),
    overlaySha256 = overlay.Sha256,
    code = blocks.Select(b => new { name = b.Item1, start = b.Item2, end = b.Item3, sha256 = Hash(overlay.Read(b.Item2, b.Item3 - b.Item2)) }),
    limits = new[] { "Original MIPS; controlled player/camera facts at 60 Hz, not a live retail playthrough.", "Sound, attachment presentation, camera effects and camera-release/hero transitions are stubbed.", "Random unrelated dust suppressed; rock post-fall wait uses the native lower bound. One trial starts with a restored command bit; other trials start clear." },
    trials,
};
string output = Path.GetFullPath(options["--out"]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
// One scalar observation per line keeps the generated trace reviewable.
json = Regex.Replace(json, "\\{\\s*\"tick\":.*?\\}", match => Regex.Replace(match.Value, @"\s+", " "), RegexOptions.Singleline);
File.WriteAllText(output, json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
Console.WriteLine($"Original-code mechanism trials: {trials.Count}; payload-free report: {output}");

static string Run(string command, IEnumerable<string> arguments, string? input = null)
{
    var info = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, UseShellExecute = false };
    foreach (string arg in arguments) info.ArgumentList.Add(arg);
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start research command.");
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (input is not null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
    if (!process.WaitForExit(120_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Research command timed out."); }
    if (process.ExitCode != 0) throw new InvalidDataException(stderr.GetAwaiter().GetResult());
    return stdout.GetAwaiter().GetResult();
}
