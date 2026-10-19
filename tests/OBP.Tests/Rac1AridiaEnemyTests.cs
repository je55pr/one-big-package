using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AridiaEnemyTests
{
    [Fact]
    public void RetailLevel2CensusPinsAdmittedEnemyPopulations()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoPaths.Root, "research", "generated", "rac1-authored-moby-census.json")));
        var classes = doc.RootElement.GetProperty("classes").EnumerateArray().ToArray();
        foreach (var (cls, count, pvar) in new[]
        {
            (Rac1AridiaEnemies.Sandshark, 115, 736),
            (Rac1AridiaEnemies.Constructorbot, 15, 576),
            (Rac1AridiaEnemies.Nest, 7, 256),
        })
        {
            var row = Assert.Single(classes, row => row.GetProperty("oClass").GetInt32() == cls);
            var placement = Assert.Single(row.GetProperty("levels").EnumerateArray().ToArray(),
                item => item.GetProperty("level").GetInt32() == Rac1AridiaEnemies.LevelId);
            Assert.Equal(count, placement.GetProperty("instances").GetInt32());
            Assert.Equal(pvar, Assert.Single(placement.GetProperty("pVarByteSizes")
                .EnumerateArray().ToArray()).GetInt32());
        }
    }

    [SkippableFact]
    public void RetailLevel2PvarsAndDeathRewardsMatchNativeClassRoutines()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(path!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(item => item.LevelId == Rac1AridiaEnemies.LevelId);
        var placements = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
        int Reward(Rac1Instances.MobyInstance moby) =>
            BinaryPrimitives.ReadInt32LittleEndian(moby.RawRecord.AsSpan(0x10));
        var sharks = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Sandshark).ToArray();
        var bots = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Constructorbot).ToArray();
        var nests = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Nest).ToArray();
        Assert.Equal([0, 1, 13], sharks.Select(Reward).Distinct().Order());
        Assert.Equal([38], bots.Select(Reward).Distinct().Order());
        Assert.Equal([0, 60], nests.Select(moby =>
            Rac1AridiaEnemies.ReadNestInitialWaitTicks(moby.PVar!)).Distinct().Order());
        Assert.All(sharks.Concat(bots), moby =>
            Assert.True(Rac1BoltCrate.RewardRange(Reward(moby)).Maximum >= Reward(moby)));
        // Native LEVEL2 shark + bot lethal branches call moby_drop_bolts
        // with (moby, 0, -1); nests instead spawn explosion/debris.
        var world = Rac1WorldImport.Build(reader, Rac1AridiaEnemies.LevelId);
        foreach (var source in world.DynamicObjects!.Where(source =>
            source.NativeClassId is Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot))
            Assert.Equal(Reward(placements[source.InstanceIndex]), Rac1EnemyMotion.RewardCentre(source));
    }

    [Fact]
    public void SandsharkEmergesPursuesAndBitesOncePerCycle()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(1, Rac1AridiaEnemies.Sandshark);
        var player = new Vector3(3, 0, 0);
        var close = new Rac1AridiaEnemyFacts(Vector3.Zero, player, true, true);
        Assert.Equal(Rac1AridiaEnemyPhase.Emerging, session.Step(1, close).Phase);
        Rac1AridiaEnemyFrame frame = default;
        for (int i = 0; i < 18; i++) frame = session.Step(1, close);
        Assert.Equal(Rac1AridiaEnemyPhase.Chasing, frame.Phase);
        Assert.True(frame.Motion.X > 0);
        int bites = 0;
        var touching = close with { PlayerPosition = new Vector3(.5f, 0, 0) };
        for (int i = 0; i < 38; i++)
        {
            frame = session.Step(1, touching);
            if (frame.DamagePlayer) bites++;
        }
        Assert.Equal(1, bites);
        Assert.Equal(Rac1AridiaEnemyPhase.Biting, frame.Phase);
        Assert.True(session.Damage(1, 1));
        Assert.False(session.Damage(1, 1));
        Assert.False(session.Step(1, touching).Alive);
    }

    [Fact]
    public void ConstructorbotFiresStationarySweepThenRecovers()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(4, Rac1AridiaEnemies.Constructorbot);
        var facts = new Rac1AridiaEnemyFacts(Vector3.Zero, new(3, 0, 0), true, true);
        int flameTicks = 0, damageTicks = 0, recoveryTicks = 0;
        for (int i = 0; i < 150; i++)
        {
            var frame = session.Step(4, facts);
            Assert.Equal(Vector3.Zero, frame.Motion);
            if (frame.FlameActive) flameTicks++;
            if (frame.DamagePlayer) damageTicks++;
            if (frame.Phase == Rac1AridiaEnemyPhase.Recovering) recoveryTicks++;
        }
        Assert.True(flameTicks > 50);
        Assert.True(damageTicks > 0);
        Assert.True(recoveryTicks >= 40);
        Assert.Equal(3, session.Health(4));
        Assert.True(session.Damage(4, 2));
        Assert.Equal(1, session.Health(4));
        Assert.True(session.Damage(4, 1));
        Assert.Equal(Rac1AridiaEnemyPhase.Dead, session.Step(4, facts).Phase);
    }

    [Fact]
    public void NestNeedsNearbyPlayerNativeWaitingSharkAndItsAuthoredTimer()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(9, Rac1AridiaEnemies.Nest, nestInitialWaitTicks: 60);
        var noPlayer = new Rac1AridiaEnemyFacts(Vector3.Zero, new(40, 0, 0), false, false,
            WaitingSharkCount: 1);
        for (int i = 0; i < 200; i++)
            Assert.False(session.Step(9, noPlayer).SpawnSandshark);
        var near = noPlayer with { PlayerAlive = true, LineOfSight = true,
            PlayerPosition = new Vector3(3, 0, 0) };
        for (int i = 0; i < 69; i++)
            Assert.False(session.Step(9, near).SpawnSandshark);
        Assert.True(session.Step(9, near).SpawnSandshark);
        for (int i = 0; i < 170; i++)
            Assert.False(session.Step(9, near with { WaitingSharkCount = 0 }).SpawnSandshark);
        Assert.True(session.Damage(9, 3));
        Assert.False(session.Step(9, near).SpawnSandshark);
    }

    [Fact]
    public void OrdinaryBuriedSharksAreNeverInventedAsNativeState26NestCandidates()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(1, Rac1AridiaEnemies.Sandshark);
        Assert.False(session.CanReleaseFromNest(1));
        Assert.False(session.ReleaseFromNest(1, new(10, 20, 3), new(15, 20, 3)));
        Assert.Equal(1, session.Count);
        var close = new Rac1AridiaEnemyFacts(Vector3.Zero, new(3, 0, 0), true, true);
        Assert.Equal(Rac1AridiaEnemyPhase.Emerging, session.Step(1, close).Phase);
        Assert.False(session.CanReleaseFromNest(1));
        Assert.Throws<ArgumentException>(() =>
            session.Register(2, Rac1AridiaEnemies.Nest, nativeWaitingState26: true));
    }

    [Fact]
    public void NestTransfersExistingSharkIntoSourceState27AndSequence11()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(1, Rac1AridiaEnemies.Sandshark, nativeWaitingState26: true);
        Assert.True(session.CanReleaseFromNest(1));
        Assert.True(session.ReleaseFromNest(1, new(10, 20, 3), new(15, 20, 3)));
        Assert.False(session.CanReleaseFromNest(1));
        Assert.False(session.ReleaseFromNest(1, Vector3.Zero, Vector3.Zero));
        var launched = session.Step(1, new(Vector3.Zero, new(15, 20, 3), false, false));
        Assert.Equal(Rac1AridiaEnemyPhase.Launched, launched.Phase);
        Assert.Equal(Rac1AridiaEnemies.NestReleasedSharkSequence, launched.Sequence);
        Assert.Equal(Rac1AridiaEnemies.NestReleaseSpeedPerTick, launched.Motion.X, 6);
        Assert.Equal(1, session.Count);
        Assert.Equal(27, Rac1AridiaEnemies.NestReleasedSharkState);
        // A released shark must not become an eligible state-26 candidate
        // merely because it loses sight of Ratchet in the host approximation.
        for (int i = 0; i < 120; i++)
            session.Step(1, new(Vector3.Zero, new(100, 0, 0), false, false));
        Assert.False(session.CanReleaseFromNest(1));
        Assert.False(session.ReleaseFromNest(1, Vector3.Zero, Vector3.UnitX));
    }

    [Fact]
    public void AuthoredNestWaitIsReadFromPvarD4WithoutAnInventedUniversalCooldown()
    {
        var pvar = new byte[256];
        Assert.Equal(0, Rac1AridiaEnemies.ReadNestInitialWaitTicks(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0xd4), 60);
        Assert.Equal(60, Rac1AridiaEnemies.ReadNestInitialWaitTicks(pvar));
        Assert.Throws<InvalidDataException>(() => Rac1AridiaEnemies.ReadNestInitialWaitTicks(new byte[64]));
    }

    [Fact]
    public void UnsupportedClassCannotRegister()
    {
        var session = new Rac1AridiaEnemySession();
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Register(10, 749));
        session.Register(10, Rac1AridiaEnemies.Nest);
        Assert.Throws<InvalidOperationException>(() => session.Register(10, Rac1AridiaEnemies.Nest));
        Assert.False(Rac1AridiaEnemies.IsEnemyClass(749));
    }
}
