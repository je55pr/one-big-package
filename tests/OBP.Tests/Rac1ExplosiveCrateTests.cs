using OBP.Core;
using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1ExplosiveCrateTests
{
    [Fact]
    public void WrenchDamageStartsShortFuseThenSourceBurstGrowsOverTwentyTicks()
    {
        var source = Source(24, repeat: false);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1ExplosiveCrateSession(runtime, (a, _) => a);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(1, session.Count);
        var hit = Hit(24, 0x00010000);
        Assert.True(runtime.CanDispatchDamage(hit));
        var trigger = runtime.DispatchDamage<Rac1ExplosiveCrateTriggered>(hit);
        Assert.Equal(5, trigger.FuseTicks);
        Assert.False(runtime.CanDispatchDamage(hit));
        for (int n = 0; n < 4; n++)
            Assert.False(session.Step(24, false).Detonated);
        var detonation = session.Step(24, false);
        Assert.True(detonation.Detonated);
        Assert.Equal(Rac1ExplosiveCrateSession.BurstState, detonation.NativeState);
        var beginning = session.Step(24, false);
        Assert.Equal(0.1, beginning.MobyBlastRadius, 5);
        Assert.Equal(0.15, beginning.PlayerBlastRadius, 5);
        Rac1ExplosiveCrateFrame last = beginning;
        for (int i = 2; i <= Rac1ExplosiveCrateSession.BurstLastStep; i++)
            last = session.Step(24, false);
        Assert.Equal(2, last.MobyBlastRadius);
        Assert.Equal(3, last.PlayerBlastRadius);
        var terminal = session.Step(24, false);
        Assert.Equal(RuntimeEntityPresence.Inactive, terminal.EntityState.Presentation.Presence);
    }

    [Fact]
    public void OriginalClass505DetonationProducesAdmittedTwoPointRatchetDamage()
    {
        var source = Source(24, repeat: false);
        var runtime = new Rac1MobyRuntimeSession();
        var explosive = new Rac1ExplosiveCrateSession(runtime, (min, _) => min);
        explosive.Register(source, RuntimeEntityState.FromAuthored(source));
        runtime.DispatchDamage<Rac1ExplosiveCrateTriggered>(Hit(24, 0x10000));

        Rac1ExplosiveCrateFrame frame = explosive.Step(24, false);
        while (!frame.Detonated)
            frame = explosive.Step(24, false);

        frame = explosive.Step(24, false);
        Assert.True(frame.MobyBlastRadius > 0);
        Assert.True(frame.PlayerBlastRadius > 0);

        var damage = Rac1ExplosiveCrateBlast.ToPlayer(24);
        Assert.True(Rac1ExplosiveCrateBlast.IsNativePlayerBlast(damage));
        Assert.False(Rac1ExplosiveCrateBlast.IsNativeMobyBlast(damage));
        var ratchet = new OBP.RAC1.Player.Rac1RatchetNanotechSession();
        Assert.Equal(2, ratchet.ApplyDamage(damage).Nanotech);
    }

    [Fact]
    public void StandingContactStartsLongFuseAndRepeatableCratesWaitForOutOfViewRearm()
    {
        var source = Source(11, repeat: true);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1ExplosiveCrateSession(runtime, (a, _) => a);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.True(session.TryTouch(11));
        Assert.False(session.TryTouch(11));
        for (int i = 0; i < 178; i++)
            Assert.False(session.Step(11, false).Detonated);
        Assert.True(session.Step(11, false).Detonated);
        for (int i = 0; i <= Rac1ExplosiveCrateSession.BurstLastStep; i++)
            session.Step(11, false);
        Assert.Equal(Rac1ExplosiveCrateSession.WaitingRespawnState,
            session.Step(11, false).NativeState);
        Assert.Equal(RuntimeEntityPresence.Inactive,
            runtime.Require(source).Presence);
        for (int i = 0; i < 40; i++)
            Assert.False(session.Step(11, false).Reappeared);
        for (int i = 0; i < 29; i++)
            Assert.False(session.Step(11, true).Reappeared);
        Assert.True(session.Step(11, true).Reappeared);
        Assert.Equal(RuntimeEntityPresence.Active, runtime.Require(source).Presence);
        Assert.True(session.TryTouch(11));
    }

    [Fact]
    public void ExplosiveDamageWithNative20000FlagSetsOneTickFuse()
    {
        var source = Source(18, repeat: false);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1ExplosiveCrateSession(runtime, (a, _) => a);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(1, runtime.DispatchDamage<Rac1ExplosiveCrateTriggered>(Hit(18, 0x20000)).FuseTicks);
        Assert.True(session.Step(18, false).Detonated);
    }

    [SkippableFact]
    public void AllExplosiveCratesHaveExpectedRetailRecordsAndRepeatFlag()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int all = 0, repeated = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
            foreach (var instance in Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level))
                .MobyInstances.Where(x => x.OClass == Rac1ExplosiveCrateSession.ClassId))
            {
                var source = Source(instance.Index, instance.RawRecord, instance.PVar!);
                var parsed = Rac1ExplosiveCrateSession.ReadAuthored(source);
                Assert.NotNull(parsed);
                if (parsed.Repeatable) repeated++;
                all++;
            }
        Assert.Equal(608, all);
        Assert.Equal(211, repeated);
        Assert.Equal(363, Rac1CrateDebris.PrimaryClass(Rac1ExplosiveCrateSession.ClassId));
    }

    [SkippableFact]
    public void RilgarExplosiveCratesHaveTheirActualFragmentModels()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        var assets = Rac1PickupAssets.Load(iso!, levelId: 5);
        Assert.Contains(363, assets.Models.Keys);
        Assert.Contains(364, assets.Models.Keys);
    }

    private static Rac1GameplayDamageEvent Hit(int id, uint flags) =>
        new(Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(new(Rac1ExplosiveCrateSession.ClassId, id)),
            1, flags);

    private static RuntimeDynamicObject Source(int id, bool repeat)
    {
        var record = new byte[0x78];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x10), 10);
        var pvar = new byte[0x100];
        pvar[0xc8] = repeat ? (byte)1 : (byte)0;
        return Source(id, record, pvar);
    }

    private static RuntimeDynamicObject Source(int id, byte[] record, byte[] pvar) =>
        new(ObpSourceGame.Rac1, Rac1ExplosiveCrateSession.ClassId, id, null, "explosive", "explosive",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads:
            [
                new RuntimeOpaquePayload(Rac1BoltCrate.InstancePayloadFormat, record),
                new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar),
            ]);
}
