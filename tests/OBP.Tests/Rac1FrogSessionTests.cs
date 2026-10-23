using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1FrogSessionTests
{
    [Fact]
    public void InitialStatesPreserveIndependentNativeShortSelectors()
    {
        var cases = new (int Path, short Carrier, short Hidden, short Lives, short Gate, int NativeState, bool Shown)[]
        {
            (-1, 0, 0, 0, 0, 8, true), (1, 0, 0, 0, 0, 3, true),
            (-1, 1, 0, 0, 0, 1, false), (-1, 0, 1, 0, 0, 15, false),
            (-1, 0, 0, 1, 0, 8, true), (-1, 0, 0, 0, 1, 8, false),
            (1, 1, 1, 1, 0, 1, false)
        };
        for (int i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var source = Source(i, c.Path, c.Carrier, c.Hidden, c.Lives, c.Gate);
            var session = new Rac1FrogSession(new(), [new[] { Vector3.Zero, Vector3.UnitX }, new[] { Vector3.Zero, Vector3.UnitX }]);
            var frame = session.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero);
            Assert.Equal(c.NativeState, frame.NativeState);
            Assert.Equal(c.Shown, frame.Presented);
            Assert.Equal(c.Lives, frame.Lives);
            if (!c.Shown)
            {
                var next = session.Step(i, Vector3.Zero, Vector3.UnitX, true);
                Assert.Equal(c.NativeState, next.NativeState);
                Assert.Equal(Vector3.Zero, next.Displacement);
            }
        }
        var gated = Source(55, gate: 1);
        var switchSession = new Rac1FrogSession(new(), []);
        Assert.True(switchSession.Register(gated, RuntimeEntityState.FromAuthored(gated), Vector3.Zero,
            switchEnabled: true).Presented);
    }

    [Fact]
    public void ApproachWindowContactAndRetreatAreBoundedAndDeterministic()
    {
        var source = Source(42);
        var session = new Rac1FrogSession(new(), []);
        session.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero);
        var pos = Vector3.Zero;
        var target = new Vector3(3, 0, 0);
        Rac1FrogFrame frame = session.Get(42);
        for (int i = 0; i < 600 && frame.NativeState != 12; i++)
        {
            frame = session.Step(42, pos, target, true);
            pos += frame.Displacement;
        }
        Assert.Equal(12, frame.NativeState);
        Assert.Equal(Rac1FrogSession.AttackSequence, frame.Sequence);
        Assert.Equal(2, Rac1FrogSession.AttackEntryDistance);
        Assert.Equal(.333f, Rac1FrogContact.NativeRadius);
        Assert.False(session.Step(42, pos, target, true, animationPosition: 9,
            attackTouchesPlayer: true).Contact);
        Assert.True(session.Step(42, pos, target, true, animationPosition: 12,
            attackTouchesPlayer: true).Contact);
        Assert.False(session.Step(42, pos, target, true, animationPosition: 13,
            attackTouchesPlayer: true).Contact);
        Assert.True(session.Step(42, pos, target, true, animationWrapped: true,
            animationPosition: 12, attackTouchesPlayer: true).Contact);
        Assert.Equal(9, session.Step(42, pos, new Vector3(20, 0, 0), false,
            animationWrapped: true).NativeState); // state12 exits only on completed clip
        Assert.Equal(13, session.Step(42, pos, new Vector3(20, 0, 0), false).NativeState);
        Assert.Equal(8, session.Step(42, Vector3.Zero, new Vector3(20, 0, 0), false).NativeState);
    }

    [Fact]
    public void FatalHitRemovesOnlyOneFrogWhileLifeModeParksInState15()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1FrogSession(runtime, []);
        var deadSource = Source(1);
        var lifeSource = Source(2, lives: 1);
        session.Register(deadSource, RuntimeEntityState.FromAuthored(deadSource), Vector3.Zero);
        session.Register(lifeSource, RuntimeEntityState.FromAuthored(lifeSource), Vector3.Zero);
        Rac1GameplayDamageEvent Hit(int id) => new(Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(new(Rac1FrogAuthored.ClassId, id)),
            1, 0x10000, owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.True(runtime.CanDispatchDamage(Hit(1)));
        var dead = runtime.DispatchDamage<Rac1FrogDamageResult>(Hit(1));
        Assert.True(dead.Lethal);
        Assert.True(dead.Frame.Terminal);
        Assert.False(dead.Frame.Presented);
        Assert.False(runtime.CanDispatchDamage(Hit(1)));
        var parked = runtime.DispatchDamage<Rac1FrogDamageResult>(Hit(2));
        Assert.True(parked.RespawnParked);
        Assert.False(parked.Lethal);
        Assert.Equal(15, parked.Frame.NativeState);
        Assert.Equal(0, parked.Frame.Lives);
        Assert.False(parked.Frame.Presented);
        Assert.False(runtime.CanDispatchDamage(Hit(2)));
        Assert.Equal(15, session.Step(2, Vector3.Zero, Vector3.UnitX, true).NativeState);
    }

    [Fact]
    public void SourceAuthoredPathDoesNotTurnIntoAnImmediateGenericChaser()
    {
        var path = new[] { Vector3.Zero, new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
        var source = Source(7, path: 0);
        var session = new Rac1FrogSession(new(), [path]);
        Assert.Equal(3, session.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero).NativeState);
        Assert.Equal(4, session.Step(7, Vector3.Zero, Vector3.UnitX * 10, true).NativeState);
        Assert.Equal(0, session.Get(7).PathIndex);
        var frame = session.Step(7, Vector3.Zero, Vector3.UnitX * 10, true);
        Assert.Equal(4, frame.NativeState);
        Assert.Equal(4, frame.Sequence);
        Assert.Equal(Vector3.Zero, frame.Displacement);
        Assert.Equal(5, session.Step(7, new Vector3(.8f, 0, 0), Vector3.UnitX * 10, true).NativeState);
        Assert.Equal(7, session.Step(7, Vector3.UnitX, Vector3.UnitX * 10, true, grounded: true).NativeState);
        Assert.Equal(8, session.Step(7, Vector3.UnitX, Vector3.UnitX * 10, true, animationWrapped: true).NativeState);
    }

    [Fact]
    public void OwnerBackedFrogContactCannotBeSpoofedOrBypassProtection()
    {
        var player = new Rac1RatchetNanotechSession();
        var hit = Rac1FrogContact.DamageToPlayer(17);
        Assert.True(Rac1FrogContact.IsAdmitted(hit));
        Assert.Equal(3, player.ApplyDamage(hit).Nanotech);
        Assert.Equal(3, player.ApplyDamage(hit).Nanotech);
        Assert.False(Rac1FrogContact.IsAdmitted(new(hit.Source, hit.Target, 1)));
        Assert.False(Rac1FrogContact.IsAdmitted(new(hit.Source, hit.Target, 1, 0x10001, owner: hit.Owner)));
    }

    [Fact]
    public void UnadmittedFrogPopulationCannotWakeOrMultiplyOverOneThousandTicks()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1FrogSession(runtime, []);
        const int census = 193;
        for (int index = 0; index < census; index++)
        {
            // Representative synthetic census: 10 carrier, 34 hidden, 17
            // switch-gated and 132 regular sources, no overlapping selectors.
            short carrier = (short)(index < 10 ? 1 : 0);
            short hidden = (short)(index is >= 10 and < 44 ? 1 : 0);
            short gated = (short)(index is >= 44 and < 61 ? 1 : 0);
            var source = Source(index, carrier: carrier, hidden: hidden, gate: gated);
            session.Register(source, RuntimeEntityState.FromAuthored(source), Vector3.Zero);
        }
        Assert.Equal(census, session.RegisteredCount);
        Assert.Equal(132, session.PresentedCount);
        for (int tick = 0; tick < 1_000; tick++)
            for (int index = 0; index < 61; index++)
            {
                var frame = session.Step(index, Vector3.Zero, Vector3.UnitX, true,
                    animationWrapped: true, animationPosition: 12, attackTouchesPlayer: true);
                Assert.False(frame.Presented);
                Assert.False(frame.Contact);
                Assert.Equal(Vector3.Zero, frame.Displacement);
            }
        Assert.Equal(132, session.PresentedCount);
        Assert.Equal(census, runtime.RegisteredCount);
    }

    [SkippableFact]
    public void OriginalRetailFrogCensusSelectorsAndPathsOnBothLevels()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0, pathless = 0, routed = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels.Where(l => l.LevelId is 6 or 10))
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var paths = Rac1Paths.Read(gameplay);
            var placements = Rac1Instances.Parse(gameplay).MobyInstances.Where(m => m.OClass == 827).ToList();
            Assert.Equal(level.LevelId == 6 ? 169 : 24, placements.Count);
            Assert.Equal(level.LevelId == 6 ? 10 : 0, placements.Count(m =>
                BinaryPrimitives.ReadInt16LittleEndian(m.PVar!.AsSpan(0x200)) != 0));
            Assert.Equal(level.LevelId == 6 ? 34 : 0, placements.Count(m =>
                BinaryPrimitives.ReadInt16LittleEndian(m.PVar!.AsSpan(0x202)) != 0));
            Assert.Equal(level.LevelId == 6 ? 12 : 0, placements.Count(m =>
                BinaryPrimitives.ReadInt16LittleEndian(m.PVar!.AsSpan(0x240)) != 0));
            Assert.Equal(level.LevelId == 6 ? 23 : 0, placements.Count(m =>
                BinaryPrimitives.ReadInt16LittleEndian(m.PVar!.AsSpan(0x242)) != 0));
            var session = new Rac1FrogSession(new(), paths);
            foreach (var moby in placements)
            {
                var obj = Source(moby.Index, pvar: moby.PVar);
                var authored = Rac1FrogAuthored.Read(obj);
                Assert.Equal(1f, authored.Health);
                Assert.InRange(authored.NativeSpeed, 4f, 5f);
                Assert.InRange(authored.PathIndex, -1, paths.Count - 1);
                var frame = session.Register(obj, RuntimeEntityState.FromAuthored(obj), Vector3.Zero);
                Assert.Equal(authored.OrdinaryHostAdmission(), frame.Presented);
                if (authored.PathIndex == -1) pathless++; else routed++;
                total++;
            }
            Assert.Equal(placements.Count, session.RegisteredCount);
            Assert.Equal(level.LevelId == 6 ? 108 : 24, session.PresentedCount);
            // Run dormant sources through a thousand native ticks: renderer,
            // collider and population must remain parked with no over-spawn.
            var dormant = placements.Where(m => !session.Get(m.Index).Presented).ToArray();
            for (int tick = 0; tick < 1_000; tick++)
                foreach (var moby in dormant)
                {
                    var frame = session.Step(moby.Index, Vector3.Zero, Vector3.UnitX, true,
                        animationWrapped: true, animationPosition: 12f, attackTouchesPlayer: true);
                    Assert.False(frame.Presented);
                    Assert.False(frame.Contact);
                    Assert.Equal(Vector3.Zero, frame.Displacement);
                }
            Assert.Equal(placements.Count, session.RegisteredCount);
            Assert.Equal(level.LevelId == 6 ? 108 : 24, session.PresentedCount);
        }
        Assert.Equal(193, total);
        Assert.Equal(167, pathless);
        Assert.Equal(26, routed);
    }

    private static RuntimeDynamicObject Source(int id, int path = -1, short carrier = 0,
        short hidden = 0, short lives = 0, short gate = 0, byte[]? pvar = null)
    {
        if (pvar is null)
        {
            pvar = new byte[Rac1FrogAuthored.PVarSize];
            BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x1f8), path);
            BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x204), -1);
            BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x200), carrier);
            BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x202), hidden);
            BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x240), lives);
            BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x242), gate);
            BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), 1);
            BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x1e4), 4);
        }
        return new("rac1", Rac1FrogAuthored.ClassId, id, null, "frog", "frog",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
