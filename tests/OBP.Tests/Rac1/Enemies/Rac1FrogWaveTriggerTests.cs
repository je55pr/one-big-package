using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1FrogWaveTriggerTests
{
    private static RuntimeDynamicObject Controller(int index, int group = 6, int volume = 0)
    {
        var p = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0), group);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), volume);
        return new(ObpSourceGame.Rac1, Rac1FrogWaveTriggers.ClassId, index, null, "trigger", "trigger",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, p)]);
    }

    private static Rac1ClipVolume Zone() => new(0, Vector3.Zero,
        new(10, 0, 0), new(0, 10, 0), new(0, 0, 10));

    [Fact]
    public void ClipGateAnimationAndNativeTwentyTickWavePreserveListedIdentity()
    {
        var waves = new Rac1FrogWaveTriggers([[], [], [], [], [], [],
            [383, 385, 435]], [383, 385, 435]);
        waves.Register(Controller(638), [Zone()], new(323, 184, 135));
        Assert.Equal(new[] { 383, 385, 435 }, waves.Members(638));
        Assert.Equal(0, waves.State(638));
        Assert.Equal(0, waves.Step(638, Vector3.UnitX * 20, true, _ => true).State);
        Assert.Equal(1, waves.Step(638, Vector3.Zero, false, _ => true).State);
        Assert.Null(waves.Step(638, Vector3.Zero, false, _ => true).LaunchInstance);
        Assert.Equal(2, waves.Step(638, Vector3.Zero, true, _ => true).State);
        var waiting = new HashSet<int>([383, 385, 435]);
        for (int wave = 0; wave < 3; wave++)
        {
            var emit = waves.Step(638, Vector3.Zero, false, waiting.Contains);
            Assert.Equal(new[] { 383, 385, 435 }[wave], emit.LaunchInstance);
            Assert.Equal(new Vector3(323, 184, 135), emit.NativeLaunchPosition);
            waiting.Remove(emit.LaunchInstance!.Value);
            Assert.Equal(20, waves.Countdown(638));
            for (int j = 0; j < 19; j++)
                Assert.Null(waves.Step(638, Vector3.Zero, false, waiting.Contains).LaunchInstance);
        }
        Assert.Null(waves.Step(638, Vector3.Zero, false, waiting.Contains).LaunchInstance);
        Assert.Equal(3, waves.State(638));
        for (int j = 0; j < 100; j++)
            Assert.Null(waves.Step(638, Vector3.Zero, true, _ => true).LaunchInstance);
    }

    [Fact]
    public void InvalidAuthoringAndProducerCandidatesStayUnadmitted()
    {
        var waves = new Rac1FrogWaveTriggers([[], [383, 385]], [383, 385]);
        Assert.Throws<InvalidDataException>(() => waves.Register(Controller(1, 0), [Zone()], Vector3.Zero));
        Assert.Throws<InvalidDataException>(() => waves.Register(Controller(2, 2), [Zone()], Vector3.Zero));
        Assert.Throws<InvalidDataException>(() => waves.Register(Controller(3, 1, 4), [Zone()], Vector3.Zero));
        waves.Register(Controller(4, 1), [Zone()], Vector3.Zero);
        Assert.Throws<InvalidOperationException>(() => waves.Register(Controller(4, 1), [Zone()], Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => waves.Register(Controller(6, 1), [Zone()], new(float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => waves.Step(4, new(float.NaN, 0, 0), false, _ => true));
        waves.Step(4, Vector3.Zero, false, _ => true);
        Assert.Equal(1, waves.RegisteredCount);
        var mismatched = new Rac1FrogWaveTriggers([[], [123, 383]], [383, 385]);
        Assert.Throws<InvalidDataException>(() => mismatched.Register(Controller(5, 1), [Zone()], Vector3.Zero));
    }

    [Fact]
    public void NativeGroupParserRejectsInvalidOffsetsIndicesAndMissingHighBit()
    {
        var gameplay = new byte[160];
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x48), 0x60);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x60), 1);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x64), 4);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x70), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x74), 383);
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x76), 385 | 0x8000);
        Assert.Equal(new[] { 383, 385 }, Rac1MobyIndexGroups.Read(gameplay, 500)[0]);

        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x76), 385);
        Assert.Throws<InvalidDataException>(() => Rac1MobyIndexGroups.Read(gameplay, 500));
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x76), 500 | 0x8000);
        Assert.Throws<InvalidDataException>(() => Rac1MobyIndexGroups.Read(gameplay, 500));
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x76), 385 | 0x8000);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x70), 4);
        Assert.Throws<InvalidDataException>(() => Rac1MobyIndexGroups.Read(gameplay, 500));
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x70), -1);
        Assert.Empty(Rac1MobyIndexGroups.Read(gameplay, 500)[0]);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x64), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => Rac1MobyIndexGroups.Read(gameplay, 500));
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x48), 0);
        Assert.Throws<InvalidDataException>(() => Rac1MobyIndexGroups.Read(gameplay, 500));
    }

    [SkippableFact]
    public void OriginalLevel6MobyGroupDirectoryAndSixFrogTriggerSelectors()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 6);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var mobys = Rac1Instances.Parse(gameplay).MobyInstances;
        var groups = Rac1MobyIndexGroups.Read(gameplay, mobys.Count);
        Assert.Equal(112, groups.Count);
        Assert.Equal(87, groups.Count(g => g.Length != 0));
        Assert.Equal(Enumerable.Range(448, 12), groups[22]);
        foreach (var (group, indices) in new (int, int[])[]
        {
            (6, [383,385,435]), (7, [382,384,386]), (8, [387,388,389,390]),
            (9, [436,437,438]), (10, [439,440,441]),
            (20, [442,443,444]), (21, [445,446,447])
        })
            Assert.Equal(indices, groups[group]);
        var frogIds = mobys.Where(m => m.OClass == 827).Select(m => m.Index).ToArray();
        var triggers = mobys.Where(m => m.OClass == Rac1FrogWaveTriggers.ClassId).ToArray();
        var waves = new Rac1FrogWaveTriggers(groups, frogIds);
        var zones = Rac1ClipVolumes.Read(gameplay);
        Assert.Equal(6, triggers.Length);
        foreach (var source in triggers)
        {
            var runtime = Controller(source.Index,
                BinaryPrimitives.ReadInt32LittleEndian(source.PVar!.AsSpan(0)),
                BinaryPrimitives.ReadInt32LittleEndian(source.PVar.AsSpan(4)));
            waves.Register(runtime, zones, new(source.Position.X, source.Position.Y, source.Position.Z));
            Assert.Equal(source.Index, runtime.InstanceIndex);
            Assert.All(waves.Members(source.Index), index =>
                Assert.Contains(index, frogIds));
        }
        Assert.Equal(new[] { 6, 7, 9, 10, 20, 21 },
            triggers.Select(t => waves.Group(t.Index)).Order().ToArray());
        Assert.Equal(12, groups[22].Count(id =>
            BinaryPrimitives.ReadInt16LittleEndian(mobys[id].PVar!.AsSpan(0x240)) == 1));
        var sourceQueen = mobys.Single(m => m.OClass == 1051);
        var pvar = sourceQueen.PVar!;
        Assert.Equal(22, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xf0)));
        Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xec)));
        Assert.Equal(59, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xf8)));
        Assert.Equal(25, BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x104)));
        int first = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xfc));
        int second = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x100));
        Assert.Equal(573, first);
        Assert.Equal(578, second);
        Assert.All(new[] { first, second }, id => Assert.Equal(1021, mobys[id].OClass));
    }
}
