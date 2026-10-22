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
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var placements = Rac1Instances.Parse(gameplay).MobyInstances;
        var groups = Rac1AridiaEnemies.ReadAuthoredGroupRoster(gameplay);
        Assert.NotEmpty(groups);
        Assert.All(groups.Values.SelectMany(members => members), member =>
            Assert.InRange(member, 0, placements.Count - 1));
        int Reward(Rac1Instances.MobyInstance moby) =>
            BinaryPrimitives.ReadInt32LittleEndian(moby.RawRecord.AsSpan(0x10));
        var sharks = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Sandshark).ToArray();
        var bots = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Constructorbot).ToArray();
        var nests = placements.Where(moby => moby.OClass == Rac1AridiaEnemies.Nest).ToArray();
        Assert.Equal([0, 1, 13], sharks.Select(Reward).Distinct().Order());
        // The six state-29 sharks include the three directly on Ratchet's
        // start coordinate (#443-445). All six link to Sandmouse #756.
        Assert.Equal([440, 441, 442, 443, 444, 445],
            sharks.Where(moby => Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(moby.PVar!) is not null)
                .Select(moby => moby.Index).OrderBy(id => id).ToArray());
        Assert.All(sharks.Where(moby => moby.Index is >= 440 and <= 445), moby =>
            Assert.Equal(756, Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(moby.PVar!)));
        Assert.Equal(Rac1AridiaEnemies.Sandmouse, placements[756].OClass);
        Assert.Equal(208, placements[756].PVar!.Length);
        Assert.Equal(526, Rac1AridiaEnemies.ReadSandmouseLinkedController(placements[756].PVar!));
        Assert.Equal(604, placements[526].OClass);
        Assert.Equal(43, BinaryPrimitives.ReadInt32LittleEndian(placements[756].PVar!.AsSpan(0x40)));
        Assert.Equal(43, BinaryPrimitives.ReadInt32LittleEndian(placements[756].PVar!.AsSpan(0x44)));
        // Authored group IDs are the +0x48 placement word copied into live
        // moby +0x21. Validate the admitted 137 placements against retail.
        Assert.All(sharks.Concat(bots).Concat(nests), moby =>
            Assert.InRange(Rac1AridiaEnemies.ReadAuthoredGroup(moby.RawRecord), -1, 254));
        Assert.Contains(nests, nest => Rac1AridiaEnemies.ReadAuthoredGroup(nest.RawRecord) >= 0);
        Assert.Equal([38], bots.Select(Reward).Distinct().Order());
        // The native 18-slot class612 updater dispatches FOUR authored
        // startup modes, not a universal flame-loop actor at level load.
        Assert.Equal(4, bots.Count(bot => Rac1AridiaConstructorbotNative.ReadInitialMode(bot.PVar!) == 0));
        Assert.Equal(1, bots.Count(bot => Rac1AridiaConstructorbotNative.ReadInitialMode(bot.PVar!) == 1));
        Assert.Equal(1, bots.Count(bot => Rac1AridiaConstructorbotNative.ReadInitialMode(bot.PVar!) == 2));
        Assert.Equal(9, bots.Count(bot => Rac1AridiaConstructorbotNative.ReadInitialMode(bot.PVar!) == 3));
        Assert.All(bots, bot =>
            Assert.Equal(18f, Rac1AridiaConstructorbotNative.ReadEngagementRadius(bot.PVar!)));
        Assert.All(bots, bot =>
            Assert.Contains(Rac1AridiaConstructorbotNative.Initialize(bot.PVar!).NativeState,
                new[] { 7, 8, 9, 10 }));
        Assert.Equal([0, 60], nests.Select(moby =>
            Rac1AridiaEnemies.ReadNestInitialWaitTicks(moby.PVar!)).Distinct().Order());
        Assert.All(sharks.Concat(bots), moby =>
            Assert.True(Rac1BoltCrate.RewardRange(Reward(moby)).Maximum >= Reward(moby)));
        // Native LEVEL2 shark + bot lethal branches call moby_drop_bolts
        // with (moby, 0, -1); nests instead spawn explosion/debris.
        var world = Rac1WorldImport.Build(reader, Rac1AridiaEnemies.LevelId);
        // These are real visible imported actors, not synthetic-only class
        // controllers. Native registration is separate from hostile collision.
        Assert.Contains(world.DynamicObjects!, source => source.InstanceIndex == 526 &&
            source.NativeClassId == Rac1AridiaClass604Session.NativeClassId && source.Meshes.Count > 0);
        Assert.Contains(world.DynamicObjects!, source => source.InstanceIndex == 756 &&
            source.NativeClassId == Rac1AridiaEnemies.Sandmouse && source.Meshes.Count > 0);
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
        var near = noPlayer with
        {
            PlayerAlive = true,
            LineOfSight = true,
            PlayerPosition = new Vector3(3, 0, 0)
        };
        for (int i = 0; i < 69; i++)
            Assert.False(session.Step(9, near).SpawnSandshark);
        Assert.True(session.Step(9, near).SpawnSandshark);
        for (int i = 0; i < 170; i++)
            Assert.False(session.Step(9, near with { WaitingSharkCount = 0 }).SpawnSandshark);
        Assert.True(session.Damage(9, 3));
        Assert.False(session.Step(9, near).SpawnSandshark);
    }

    [Fact]
    public void NativeNestAnimationClockMustReachFrameTenWithoutUsingElapsedTicks()
    {
        var nest = new Rac1AridiaEnemySession();
        nest.Register(9, Rac1AridiaEnemies.Nest, nestInitialWaitTicks: 1);
        var near = new Rac1AridiaEnemyFacts(Vector3.Zero, Vector3.UnitX, true, true,
            WaitingSharkCount: 1, NestAnimationTime: 100);
        // A stale idle animation sample cannot release in the same step that
        // the native windup sequence is selected.
        var first = nest.Step(9, near);
        Assert.Equal(Rac1AridiaEnemyPhase.WindingUp, first.Phase);
        Assert.False(first.SpawnSandshark);
        for (int i = 0; i < 40; i++)
            Assert.False(nest.Step(9, near with { NestAnimationTime = 9.8f }).SpawnSandshark);
        var release = nest.Step(9, near with { NestAnimationTime = 10.1f });
        Assert.True(release.SpawnSandshark);
        Assert.Equal(Rac1AridiaEnemyPhase.Guarding, release.Phase);
    }

    [Fact]
    public void NativeLinkedSandmouseState29BlocksEvenPointBlankAggroUntilState5()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(443, Rac1AridiaEnemies.Sandshark, linkedActivationMobyIndex: 756);
        var pointBlank = new Rac1AridiaEnemyFacts(Vector3.Zero, new(.2f, 0, 0), true, true);
        Assert.Equal(756, session.LinkedActivationMobyIndex(443));
        for (int i = 0; i < 120; i++)
            Assert.Equal(Rac1AridiaEnemyPhase.Buried, session.Step(443, pointBlank).Phase);
        Assert.Equal(Rac1AridiaEnemyPhase.Buried,
            session.Step(443, pointBlank with { LinkedMobyNativeState = 4 }).Phase);
        Assert.False(session.CanReleaseFromNest(443));
        Assert.Equal(Rac1AridiaEnemyPhase.Emerging,
            session.Step(443, pointBlank with { LinkedMobyNativeState = 5 }).Phase);
        // Once the native linkage has been satisfied, the state must remain
        // unblocked even if the Sandmouse later changes its own state.
        for (int i = 0; i < 20; i++)
            session.Step(443, pointBlank with { LinkedMobyNativeState = 0 });
        Assert.NotEqual(Rac1AridiaEnemyPhase.Buried, session.Step(443, pointBlank).Phase);
    }

    [Fact]
    public void AridiaSandsharkLinkedStatePvarReaderRejectsInvalidInputs()
    {
        var pvar = new byte[736];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x258), -1);
        Assert.Null(Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x258), 756);
        Assert.Equal(756, Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x258), -2);
        Assert.Throws<InvalidDataException>(() => Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(pvar));
        Assert.Throws<InvalidDataException>(() => Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(new byte[12]));
        var session = new Rac1AridiaEnemySession();
        Assert.Throws<ArgumentException>(() =>
            session.Register(3, Rac1AridiaEnemies.Nest, linkedActivationMobyIndex: 756));
    }

    [Fact]
    public void SandmouseControllerLinkRejectsMalformedOrMissingNativePvar()
    {
        var pvar = new byte[208];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x48), 526);
        Assert.Equal(526, Rac1AridiaEnemies.ReadSandmouseLinkedController(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x48), -1);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaEnemies.ReadSandmouseLinkedController(pvar));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaEnemies.ReadSandmouseLinkedController(new byte[0x48]));
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
    public void NativeGroupReaderRequiresValidSignedAuthoredIndex()
    {
        var record = new byte[0x78];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x48), -1);
        Assert.Equal(-1, Rac1AridiaEnemies.ReadAuthoredGroup(record));
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x48), 8);
        Assert.Equal(8, Rac1AridiaEnemies.ReadAuthoredGroup(record));
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x48), 255);
        Assert.Equal(-1, Rac1AridiaEnemies.ReadAuthoredGroup(record));
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0x48), 256);
        Assert.Throws<InvalidDataException>(() => Rac1AridiaEnemies.ReadAuthoredGroup(record));
        Assert.Throws<InvalidDataException>(() => Rac1AridiaEnemies.ReadAuthoredGroup(new byte[8]));
    }

    [Fact]
    public void NativeNestGroupCannotReleaseBuriedOrWrongGroupSharks()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(10, Rac1AridiaEnemies.Nest, groupId: 3);
        session.Register(11, Rac1AridiaEnemies.Nest, groupId: 4);
        session.Register(20, Rac1AridiaEnemies.Sandshark, groupId: 3);
        session.Register(21, Rac1AridiaEnemies.Sandshark, groupId: 4, nativeWaitingState26: true);
        session.Register(22, Rac1AridiaEnemies.Sandshark, groupId: 3, nativeWaitingState26: true);
        session.Register(23, Rac1AridiaEnemies.Sandshark, groupId: 3, nativeWaitingState26: true);
        Assert.Equal(2, session.WaitingSharkCountForNest(10));
        Assert.Equal(22, session.FindWaitingSharkForNest(10));
        Assert.Equal(21, session.FindWaitingSharkForNest(11));
        Assert.False(session.ReleaseFromNest(10, 21, Vector3.Zero, Vector3.UnitX));
        Assert.False(session.ReleaseFromNest(10, 20, Vector3.Zero, Vector3.UnitX));
        Assert.True(session.ReleaseFromNest(10, 22, Vector3.Zero, Vector3.UnitX));
        Assert.Equal(23, session.FindWaitingSharkForNest(10));
        Assert.Equal(1, session.WaitingSharkCountForNest(10));
        Assert.Equal(6, session.Count);
    }

    [Fact]
    public void RetailGroupRosterReaderPreservesSourceOrderAndTermination()
    {
        var gameplay = new byte[0xc0];
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x48), 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x80), 3);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x84), 8);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x90), 0);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x94), -1);
        BinaryPrimitives.WriteInt32LittleEndian(gameplay.AsSpan(0x98), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x9c), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0x9e), 0x8016);
        BinaryPrimitives.WriteUInt16LittleEndian(gameplay.AsSpan(0xa0), 0x8009);
        var roster = Rac1AridiaEnemies.ReadAuthoredGroupRoster(gameplay);
        Assert.Equal([23, 22], roster[0]);
        Assert.Equal([9], roster[2]);
        Assert.False(roster.ContainsKey(1));

        var session = new Rac1AridiaEnemySession();
        session.Register(10, Rac1AridiaEnemies.Nest, groupId: 0);
        session.Register(22, Rac1AridiaEnemies.Sandshark, groupId: 0, nativeWaitingState26: true);
        session.Register(23, Rac1AridiaEnemies.Sandshark, groupId: 0, nativeWaitingState26: true);
        session.SetAuthoredGroupRoster(roster);
        Assert.Equal(23, session.FindWaitingSharkForNest(10));
        Assert.True(session.ReleaseFromNest(10, 23, Vector3.Zero, Vector3.UnitX));
        Assert.Equal(22, session.FindWaitingSharkForNest(10));
    }

    [Fact]
    public void DeadGroupSharkOnlyRetiresToNative26WithLivingNestAndReusesIdentity()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(10, Rac1AridiaEnemies.Nest, groupId: 7);
        session.Register(20, Rac1AridiaEnemies.Sandshark, groupId: 7);
        session.Register(21, Rac1AridiaEnemies.Sandshark, groupId: 8);
        session.Register(22, Rac1AridiaEnemies.Sandshark);
        Assert.False(session.RetireToNestPool(20));
        Assert.True(session.Damage(20, 1));
        Assert.True(session.Damage(21, 1));
        Assert.True(session.Damage(22, 1));
        Assert.False(session.RetireToNestPool(21));
        Assert.False(session.RetireToNestPool(22));
        Assert.True(session.RetireToNestPool(20));
        Assert.True(session.CanReleaseFromNest(20));
        Assert.Equal(0, session.Health(20));
        Assert.Equal(20, session.FindWaitingSharkForNest(10));
        Assert.True(session.ReleaseFromNest(10, 20, Vector3.UnitY, Vector3.UnitX));
        Assert.Equal(1, session.Health(20));
        Assert.False(session.CanReleaseFromNest(20));
        Assert.Equal(4, session.Count);
    }

    [Fact]
    public void OpeningPopulationDoesNotAdmitEarlyNestCandidatesOrSandmouseLinkedSharks()
    {
        var session = new Rac1AridiaEnemySession();
        session.Register(100, Rac1AridiaEnemies.Nest, groupId: 1, nestInitialWaitTicks: 0);
        for (int i = 0; i < 115; i++)
            session.Register(200 + i, Rac1AridiaEnemies.Sandshark, groupId: 1,
                linkedActivationMobyIndex: i < 6 ? 756 : null);
        var besidePlayer = new Rac1AridiaEnemyFacts(Vector3.Zero, new(.1f, 0, 0), true, true);
        for (int tick = 0; tick < 180; tick++)
        {
            foreach (int id in Enumerable.Range(200, 6))
                Assert.Equal(Rac1AridiaEnemyPhase.Buried, session.Step(id, besidePlayer).Phase);
            Assert.False(session.Step(100, besidePlayer with
            {
                WaitingSharkCount = session.WaitingSharkCountForNest(100)
            }).SpawnSandshark);
        }
        Assert.Null(session.FindWaitingSharkForNest(100));
        Assert.Equal(116, session.Count);
        foreach (int id in Enumerable.Range(200, 6))
            Assert.Equal(Rac1AridiaEnemyPhase.Emerging,
                session.Step(id, besidePlayer with { LinkedMobyNativeState = 5 }).Phase);
    }

    [Fact]
    public void NewLevelSessionClearsReleasedAndPooledStatesOnRetry()
    {
        static Rac1AridiaEnemySession NewSession()
        {
            var session = new Rac1AridiaEnemySession();
            session.Register(1, Rac1AridiaEnemies.Nest, groupId: 1);
            session.Register(2, Rac1AridiaEnemies.Sandshark, groupId: 1);
            session.Register(3, Rac1AridiaEnemies.Sandshark, groupId: 1,
                linkedActivationMobyIndex: 756);
            session.SetAuthoredGroupRoster(new Dictionary<int, int[]> { [1] = [1, 2, 3] });
            return session;
        }

        var first = NewSession();
        Assert.True(first.Damage(2, 1));
        Assert.True(first.RetireToNestPool(2));
        Assert.True(first.ReleaseFromNest(1, 2, Vector3.Zero, Vector3.UnitX));
        Assert.False(first.CanReleaseFromNest(2));
        var retry = NewSession();
        Assert.Equal(3, retry.Count);
        Assert.Null(retry.FindWaitingSharkForNest(1));
        Assert.False(retry.CanReleaseFromNest(2));
        var touching = new Rac1AridiaEnemyFacts(Vector3.Zero, Vector3.Zero, true, true);
        Assert.Equal(Rac1AridiaEnemyPhase.Buried, retry.Step(3, touching).Phase);
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
