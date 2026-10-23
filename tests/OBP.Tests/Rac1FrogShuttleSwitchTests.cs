using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1FrogShuttleSwitchTests
{
    [Fact]
    public void WaitingShuttleVolumesChooseExclusiveFrogPopulationWithoutFreeingDormantMobs()
    {
        var volumes = new[]
        {
            Box(0, 0), Box(1, 10), Box(2, 20),
        };
        var shuttle = Rac1FrogShuttleSwitch.FromRetail([Shuttle()], volumes)!;
        var runtime = new Rac1MobyRuntimeSession();
        var frogs = new Rac1FrogSession(runtime, []);
        foreach (var (id, carrier, hidden, gate, lives) in new (int, short, short, short, short)[]
        {
            (10, 0, 0, 0, 0), (11, 0, 0, 1, 0), (12, 1, 0, 1, 0),
            (13, 0, 1, 1, 0), (14, 0, 0, 1, 1),
        })
        {
            var source = Frog(id, carrier, hidden, gate, lives);
            frogs.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero);
        }
        Assert.Equal(1, frogs.PresentedCount);
        Assert.True(shuttle.ObserveWaitingShuttle(new(20, 0, 0)));
        Assert.Equal(1, shuttle.SelectedStop);
        Assert.True(shuttle.SwitchEnabled);
        Assert.Equal(new[] { 10, 11, 14 }, frogs.ApplyShuttleSwitch(shuttle.SwitchEnabled).Order());
        Assert.Equal(2, frogs.PresentedCount);
        Assert.False(frogs.Get(10).Presented);
        Assert.True(frogs.Get(11).Presented);
        Assert.False(frogs.Get(12).Presented);
        Assert.False(frogs.Get(13).Presented);
        Assert.True(frogs.Get(14).Presented);
        Assert.Empty(frogs.ApplyShuttleSwitch(true));
        Assert.False(shuttle.ObserveWaitingShuttle(new(0, 0, 0)));
        Assert.True(shuttle.AcceptShuttleArrival(0));
        Assert.Equal(new[] { 10, 11, 14 }, frogs.ApplyShuttleSwitch(shuttle.SwitchEnabled).Order());
        Assert.False(frogs.Get(11).Presented);
        Assert.True(frogs.Get(10).Presented);
        Assert.True(shuttle.ObserveWaitingShuttle(new(0, 0, 0)));
        Assert.Equal(2, shuttle.SelectedStop);
        Assert.True(shuttle.ObserveWaitingShuttle(new(10, 0, 0)));
        Assert.Equal(0, shuttle.SelectedStop);
        Assert.Equal(5, frogs.RegisteredCount);
        Assert.Equal(5, runtime.RegisteredCount);
    }

    [Fact]
    public void WrongShuttleDataAndInvalidStopCannotUnlockAFrog()
    {
        Assert.Null(Rac1FrogShuttleSwitch.FromRetail([], [Box(0, 0)]));
        Assert.Throws<InvalidDataException>(() => Rac1FrogShuttleSwitch.FromRetail(
            [Shuttle(0, 1, 2)], [Box(0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1FrogShuttleSwitch.FromRetail([Shuttle()], [Box(0, 0), Box(1, 10), Box(2, 20)])!
                .AcceptShuttleArrival(3));
    }

    [Fact]
    public void ShuttleSwitchNeverRespawnsAParkedOrTerminalFrog()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var frogs = new Rac1FrogSession(runtime, []);
        var source = Frog(33, 0, 0, 1, 1);
        frogs.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero);
        Assert.False(frogs.Get(33).Presented);
        frogs.ApplyShuttleSwitch(true);
        Assert.True(frogs.Get(33).Presented);
        var hit = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(new(Rac1FrogAuthored.ClassId, 33)),
            1, 0x10000, owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.True(runtime.DispatchDamage<Rac1FrogDamageResult>(hit).RespawnParked);
        Assert.False(frogs.Get(33).Presented);
        Assert.Equal(15, frogs.Get(33).NativeState);
        frogs.ApplyShuttleSwitch(false);
        Assert.Empty(frogs.ApplyShuttleSwitch(true));
        Assert.False(frogs.Get(33).Presented);
        Assert.False(runtime.CanDispatchDamage(hit));
        Assert.Equal(1, frogs.RegisteredCount);
    }

    [SkippableFact]
    public void OriginalNebulaShuttleStopSelectorsResolveActualClipVolumes()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 6);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var volumes = Rac1ClipVolumes.Read(gameplay);
        var sources = Rac1Instances.Parse(gameplay).MobyInstances.Where(m => m.OClass == 1109)
            .Select(m => new RuntimeDynamicObject("rac1", 1109, m.Index, null, "shuttle", "shuttle",
                new RuntimeObjectTransform(new double[16]), [],
                NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, m.PVar!)]))
            .ToArray();
        Assert.Single(sources);
        var shuttle = Rac1FrogShuttleSwitch.FromRetail(sources, volumes);
        Assert.NotNull(shuttle);
        Assert.Equal(sources[0].InstanceIndex, shuttle.ShuttleInstance);
        Assert.Equal(0, shuttle.SelectedStop);
    }

    private static Rac1ClipVolume Box(int id, float x) =>
        new(id, new(x, 0, 0), Vector3.UnitX * .5f, Vector3.UnitY * .5f, Vector3.UnitZ * .5f);

    private static RuntimeDynamicObject Shuttle(int a = 0, int b = 1, int c = 2)
    {
        var pvar = new byte[0x118];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x100), a);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x104), b);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x108), c);
        return new("rac1", 1109, 55, null, "shuttle", "shuttle",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }

    private static RuntimeDynamicObject Frog(int id, short carrier, short hidden, short gate, short lives)
    {
        var pvar = new byte[0x290];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1f8), -1);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x204), -1);
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x200), carrier);
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x202), hidden);
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x242), gate);
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x240), lives);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), 1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x1e4), 4);
        return new("rac1", Rac1FrogAuthored.ClassId, id, null, "frog", "frog",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
