using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1NovalisMechanismTests
{
    private static JsonDocument Witness() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "research/generated/rac1-novalis-mechanisms.json")));
    public static IEnumerable<object[]> OriginalTrials()
    {
        using var doc = Witness();
        for (int i = 0; i < doc.RootElement.GetProperty("trials").GetArrayLength(); i++) yield return [i];
    }

    [Theory]
    [MemberData(nameof(OriginalTrials))]
    public void ReplaysOriginalMipsMechanismTrace(int index)
    {
        using var doc = Witness();
        var trial = doc.RootElement.GetProperty("trials")[index];
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
