using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1NovalisMechanismTests
{
    private static readonly (int Instance, int Class, bool Restored)[] TrialPlan =
    [
        (711, 701, false), (711, 701, true), (712, 701, false), (713, 701, false),
        (714, 703, false), (715, 703, false),
        (727, 715, false), (728, 715, false), (729, 715, false), (730, 715, false), (731, 715, false),
        (893, 768, false), (894, 768, false), (895, 768, false), (896, 768, false),
        (897, 769, false), (898, 769, false), (899, 769, false), (900, 769, false),
    ];
    private static string WitnessPath => Path.Combine(RepoPaths.Root, "research/generated/rac1-novalis-mechanisms.json");
    private static JsonDocument Witness()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(WitnessPath));
        try { RequireWitnessCohorts(doc.RootElement); return doc; }
        catch { doc.Dispose(); throw; }
    }
    public static IEnumerable<object[]> OriginalTrials() => TrialPlan.Select(trial => new object[] { trial.Instance, trial.Restored });

    [Theory]
    [MemberData(nameof(OriginalTrials))]
    public void ReplaysOriginalMipsMechanismTrace(int instance, bool restored)
    {
        using var doc = Witness();
        var trial = doc.RootElement.GetProperty("trials").EnumerateArray().Single(trial =>
            trial.GetProperty("instance").GetInt32() == instance && trial.GetProperty("restoredFallen").GetBoolean() == restored);
        Rac1NovalisMechanism mechanism = Create(Source(trial), Vector(trial.GetProperty("trigger")), trial.GetProperty("restoredFallen").GetBoolean());
        var samples = trial.GetProperty("samples").EnumerateArray().ToDictionary(s => s.GetProperty("tick").GetInt32());
        int tick = 0;
        foreach (var segment in trial.GetProperty("segments").EnumerateArray())
        {
            Vector3 player = Vector(segment.GetProperty("player")), camera = Vector(segment.GetProperty("camera"));
            if (mechanism is Rac1NovalisFallingRock rock) rock.PlayerRunMode = segment.GetProperty("run").GetInt32();
            for (int n = segment.GetProperty("ticks").GetInt32(); n > 0; n--, tick++)
            {
                mechanism.Step(player, camera);
                if (!samples.TryGetValue(tick, out var expected)) continue;
                Assert.True(mechanism.State == expected.GetProperty("state").GetInt32(), $"class {mechanism.NativeClassId} instance {mechanism.InstanceIndex} tick {tick}: state {mechanism.State}, expected {expected.GetProperty("state")}");
                Assert.InRange(Vector3.Distance(mechanism.Position, Vector(expected.GetProperty("position"))), 0, .0005f);
                Assert.Equal(expected.GetProperty("sound").GetInt32(), mechanism.Sound ?? -1);
                var (end, scalar, timer) = mechanism switch
                {
                    Rac1NovalisPiston p => (p.End, p.Rate, p.Timer),
                    Rac1NovalisProximityDoor d => (0, d.Progress, 0),
                    Rac1NovalisFallingRock r => (0, r.FallVelocity, r.Timer),
                    _ => throw new InvalidOperationException(),
                };
                Assert.Equal(expected.GetProperty("end").GetInt32(), end);
                Assert.Equal(expected.GetProperty("timer").GetInt32(), timer);
                Assert.InRange(MathF.Abs(scalar - expected.GetProperty("scalar").GetSingle()), 0, .000001f);
            }
        }
        Assert.True(samples.ContainsKey(tick - 1), "Trace must include its final state.");
    }

    [SkippableFact]
    public void RetailOverlayAndAllEighteenAuthoredMechanismsMatchWitness()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 1);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances;
        var volumes = Rac1ClipVolumes.Read(gameplay);
        using var doc = Witness();
        Assert.Equal(doc.RootElement.GetProperty("overlaySha256").GetString(), overlay.Sha256);
        foreach (var block in doc.RootElement.GetProperty("code").EnumerateArray())
        {
            int start = block.GetProperty("start").GetInt32(), end = block.GetProperty("end").GetInt32();
            Assert.Equal(block.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(overlay.Read(start, end - start))).ToLowerInvariant());
        }
        Assert.Equal(7, instances.Count(m => m.OClass is 703 or 715));
        Assert.Equal(8, instances.Count(m => m.OClass is 768 or 769));
        Assert.Equal(3, instances.Count(m => m.OClass == 701));
        foreach (var trial in doc.RootElement.GetProperty("trials").EnumerateArray())
        {
            var actual = instances.Single(m => m.Index == trial.GetProperty("instance").GetInt32());
            var expected = Source(trial);
            Assert.Equal(expected.OClass, actual.OClass);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Rotation.Z, actual.Rotation.Z);
            var mechanism = Create(actual, volumes[18].Centre);
            switch (mechanism)
            {
                case Rac1NovalisProximityDoor d: Assert.Equal(trial.GetProperty("reach").GetSingle(), d.Reach); break;
                case Rac1NovalisPiston p:
                    Assert.Equal(trial.GetProperty("initialEnd").GetInt32(), p.InitialEnd);
                    Assert.Equal(trial.GetProperty("height").GetSingle(), p.Height);
                    Assert.Equal((int)(trial.GetProperty("seconds").GetSingle() * 60), p.TravelTicks);
                    break;
                case Rac1NovalisFallingRock r:
                    Assert.Equal(trial.GetProperty("delay").GetInt32(), r.DelayTicks);
                    Assert.Equal(Vector(trial.GetProperty("trigger")), r.Trigger);
                    break;
            }
        }
    }

    [Fact]
    public void UnboundedOrWrongAuthoredParametersFailClosed()
    {
        using var doc = Witness();
        var source = Source(doc.RootElement.GetProperty("trials").EnumerateArray().First(t => t.GetProperty("nativeClassId").GetInt32() == 703));
        Assert.Throws<InvalidDataException>(() => new Rac1NovalisPiston(source with { PVar = new byte[8] }));
        BinaryPrimitives.WriteSingleLittleEndian(source.PVar!.AsSpan(0xb0), float.NaN);
        Assert.Throws<InvalidDataException>(() => new Rac1NovalisPiston(source));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("missing-code")]
    [InlineData("renamed-code")]
    [InlineData("empty-range")]
    [InlineData("missing-trial")]
    [InlineData("duplicate-trial")]
    [InlineData("short-segment")]
    [InlineData("missing-sample")]
    [InlineData("duplicate-sample")]
    public void WitnessRequiresEveryRetainedMechanismTrialAndCheckpoint(string fault)
    {
        var witness = JsonNode.Parse(File.ReadAllText(WitnessPath))!;
        var code = witness["code"]!.AsArray();
        var trials = witness["trials"]!.AsArray();
        switch (fault)
        {
            case "complete": break;
            case "missing-code": code.Clear(); break;
            case "renamed-code": code[0]!["name"] = "replacement"; break;
            case "empty-range": code[0]!["end"] = code[0]!["start"]!.GetValue<int>(); break;
            case "missing-trial": trials.RemoveAt(0); break;
            case "duplicate-trial": trials[1] = trials[0]!.DeepClone(); break;
            case "short-segment": trials[0]!["segments"]![0]!["ticks"] = 1; break;
            case "missing-sample": trials[0]!["samples"]!.AsArray().RemoveAt(0); break;
            case "duplicate-sample": trials[0]!["samples"]![1] = trials[0]!["samples"]![0]!.DeepClone(); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "Unknown witness fault.");
        }
        void Check() => RequireWitnessCohorts(JsonSerializer.SerializeToElement(witness));
        if (fault == "complete") Check();
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(Check);
    }

    private static void RequireWitnessCohorts(JsonElement witness)
    {
        // Input families and code windows are retained independently in
        // tools/OBP.Rac1NovalisRouteProbe/Program.cs. Checkpoint ticks pin the
        // existing original-instruction observations, including transition/sound samples.
        var code = witness.GetProperty("code").EnumerateArray().Select(block => (
            Name: block.GetProperty("name").GetString()!, Start: block.GetProperty("start").GetInt32(),
            End: block.GetProperty("end").GetInt32())).ToArray();
        Assert.Equal(new[] { ("approachDoors", 0x2fed68, 0x2ff028), ("pistons", 0x2f95c0, 0x2f9810),
            ("staircaseRocks", 0x2f9080, 0x2f95c0) }, code);
        var trials = witness.GetProperty("trials").EnumerateArray().ToArray();
        Assert.Equal(TrialPlan.OrderBy(trial => trial.Instance).ThenBy(trial => trial.Restored),
            trials.Select(trial => (Instance: trial.GetProperty("instance").GetInt32(),
                Class: trial.GetProperty("nativeClassId").GetInt32(), Restored: trial.GetProperty("restoredFallen").GetBoolean()))
                .OrderBy(trial => trial.Instance).ThenBy(trial => trial.Restored));
        foreach (var trial in trials)
        {
            int instance = trial.GetProperty("instance").GetInt32();
            int nativeClass = trial.GetProperty("nativeClassId").GetInt32();
            bool restored = trial.GetProperty("restoredFallen").GetBoolean();
            int[] segments = nativeClass switch { 701 => [2, 1, 1, 210], 703 or 715 => [650], _ => [1, 1, 1, 24, 6, 5, 24, 25] };
            Assert.Equal(segments, trial.GetProperty("segments").EnumerateArray().Select(segment => segment.GetProperty("ticks").GetInt32()));
            int ticks = segments.Sum();
            int[] events = nativeClass switch
            {
                701 when restored => [],
                701 when instance == 711 => [49, 118],
                701 when instance == 712 => [64, 133],
                701 => [77, 146],
                703 => [165, 285, 495, 615],
                715 => [142, 262, 284, 404, 426, 546, 568],
                _ => [24, 33, 38, 43, 62, 82],
            };
            var checkpoints = Enumerable.Range(0, 6).Concat(Enumerable.Range(0, ticks).Where(tick => tick % 30 == 0))
                .Append(ticks - 1).Concat(events).Distinct().Order();
            Assert.Equal(checkpoints, trial.GetProperty("samples").EnumerateArray().Select(sample => sample.GetProperty("tick").GetInt32()).Order());
        }
    }

    private static Rac1NovalisMechanism Create(Rac1Instances.MobyInstance source, Vector3 trigger, bool restored = false) => source.OClass switch
    {
        768 or 769 => new Rac1NovalisProximityDoor(source),
        703 or 715 => new Rac1NovalisPiston(source),
        701 => new Rac1NovalisFallingRock(source, Enumerable.Range(0, 19).Select(i => new Rac1ClipVolume(i, trigger, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)).ToArray()) { RestoredFallen = restored },
        _ => throw new InvalidDataException(),
    };

    private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    private static Rac1Instances.MobyInstance Source(JsonElement trial)
    {
        int cls = trial.GetProperty("nativeClassId").GetInt32();
        byte[] p = new byte[cls is 768 or 769 ? 0x20 : cls == 701 ? 0x90 : 0xc0];
        if (cls is 768 or 769) BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0x14), trial.GetProperty("reach").GetSingle());
        else if (cls == 701) BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x88), trial.GetProperty("delay").GetInt16());
        else
        {
            p[0xa4] = (byte)trial.GetProperty("initialEnd").GetInt32();
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0xa8), trial.GetProperty("height").GetSingle());
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0xb0), trial.GetProperty("seconds").GetSingle());
        }
        var home = Vector(trial.GetProperty("home"));
        return new(trial.GetProperty("instance").GetInt32(), cls, 1, (home.X, home.Y, home.Z), (0, 0, trial.GetProperty("yaw").GetSingle()), null, 0, [], p);
    }
}
