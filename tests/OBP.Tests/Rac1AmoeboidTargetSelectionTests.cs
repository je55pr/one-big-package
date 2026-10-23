using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AmoeboidTargetSelectionTests
{
    [Fact]
    public void RetailClass623CandidateMustBeLiveAndCloserThanCurrentHero()
    {
        var origin = Vector3.Zero;
        var hero = new Vector3(50, 0, 0);
        Rac1AmoeboidTargetSelection.Candidate[] candidates =
        [
            new(20, 623, 2, new(22, 0, 0), true, 0),
            new(21, 623, 2, new(8, 0, 0), false, 0),
            new(22, 623, 2, new(4, 0, 0), true, 127),
            new(23, 623, 3, new(2, 0, 0), true, 0),
            new(24, 572, 2, new(1, 0, 0), true, 0)
        ];

        Assert.Equal(20, Rac1AmoeboidTargetSelection.Select(2, origin, hero, 35, candidates)?.InstanceIndex);
        Assert.Null(Rac1AmoeboidTargetSelection.Select(2, origin, new(5, 0, 0), 35, candidates));
        Assert.Null(Rac1AmoeboidTargetSelection.Select(2, origin, hero, 20, candidates));
        Assert.Null(Rac1AmoeboidTargetSelection.Select(-1, origin, hero, 35, candidates));
        Assert.Null(Rac1AmoeboidTargetSelection.Select(255, origin, hero, 35, candidates));
    }

    [Fact]
    public void SourceSearchLimit120IsStrictAndPreservesNativeListOrderOnTies()
    {
        Rac1AmoeboidTargetSelection.Candidate[] candidates =
        [
            new(30, 623, 77, new(120, 0, 0), true, 0),
            new(31, 623, 77, new(15, 0, 0), true, 0),
            new(32, 623, 77, new(-15, 0, 0), true, 0)
        ];
        Assert.Equal(31, Rac1AmoeboidTargetSelection.Select(77, Vector3.Zero,
            new(200, 0, 0), 200, candidates)?.InstanceIndex);
        Assert.Null(Rac1AmoeboidTargetSelection.Select(77, Vector3.Zero,
            new(200, 0, 0), 10, [candidates[0]]));
    }

    [Fact]
    public void LungeAtClass623PreservesVictimAndOwnerAcrossTargetSwitches()
    {
        var latch = new Rac1AmoeboidLungeTargetLatch();
        var moby = Rac1GameplayEntityRef.Moby(new(623, 984));
        var ratchet = Rac1GameplayEntityRef.Player;

        Assert.Equal(moby, latch.Observe(931, Rac1AmoeboidFamilySession.LungeState, moby));
        Assert.True(latch.AcceptsContact(931, moby));
        Assert.False(latch.AcceptsContact(931, ratchet));
        Assert.Equal(moby, latch.Observe(931, Rac1AmoeboidFamilySession.LungeState, ratchet));

        var envelope = Rac1AmoeboidAttack.ToTarget(572, 931, moby);
        Assert.Equal(moby, envelope.Target);
        Assert.Equal(Rac1GameplayEntityRef.Moby(new(572, 931)), envelope.Source);
        Assert.Equal(envelope.Source, envelope.Owner?.RuntimeEntity);
        Assert.True(Rac1AmoeboidAttack.IsNativeAttack(envelope));
        Assert.False(Rac1AmoeboidAttack.IsNativePlayerAttack(envelope));
        Assert.Throws<ArgumentException>(() => Rac1AmoeboidAttack.ToTarget(
            572, 931, Rac1GameplayEntityRef.PlayerWrench));

        Assert.Null(latch.Observe(931, Rac1AmoeboidFamilySession.AnimationExitState, ratchet));
        Assert.True(latch.AcceptsContact(931, ratchet));
        Assert.Equal(ratchet, latch.Observe(931, Rac1AmoeboidFamilySession.LungeState, ratchet));
        Assert.False(latch.AcceptsContact(931, moby));
        latch.Clear();
        Assert.Null(latch.Current(931));
    }

    [SkippableFact]
    public void RetailClass623StateZeroInitializesNativeTargetEligibilityFlag()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay source, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Read(address, 4));

        // Rilgar class-623 updater at 0x301f48 dispatches native Moby
        // state at +0x20 through the table at 0x2186b0. State 0 adds
        // flag 0x1000 to native Moby flags at +0x34.
        Assert.Equal(0x00302080u, Word(overlay, 0x002186b0));
        Assert.Equal(0x92640020u, Word(overlay, 0x0030204c));
        Assert.Equal(0x96630034u, Word(overlay, 0x00302080));
        Assert.Equal(0x34631000u, Word(overlay, 0x0030208c));
        Assert.Equal(0xa6630034u, Word(overlay, 0x00302090));
        // The +0x2b4 gate moves to state 12 and clears the targetable bit.
        Assert.Equal(0x8e2202b4u, Word(overlay, 0x0030215c));
        Assert.Equal(0xa2630020u, Word(overlay, 0x0030217c));
        Assert.Equal(0x3042efffu, Word(overlay, 0x00302180));
        Assert.Equal(0xa6620034u, Word(overlay, 0x00302188));
        // The eight-byte +0x2b8 sentinel chooses state 15 (ineligible)
        // or state 1 (initially eligible) when +0x2b4 is zero.
        Assert.Equal(0xde2302b8u, Word(overlay, 0x0030219c));
        Assert.Equal(0x1062000eu, Word(overlay, 0x003021a4));
        Assert.Equal(0x3042efffu, Word(overlay, 0x003021b4));
        Assert.Equal(0xa2630020u, Word(overlay, 0x003021b8));
        Assert.Equal(0xa6620034u, Word(overlay, 0x003021c0));
        Assert.Equal(0xa2620020u, Word(overlay, 0x003021ec));
        // Native state 15 is not permanently dormant: the updater tests
        // its authored linked Moby at +0x2b8 (live flag at Moby+0xbc),
        // then an alternate +0x2bc gate, and can reenter state 1 while
        // restoring the 0x1000 native target flag.
        Assert.Equal(0x0030282cu, Word(overlay, 0x002186ec));
        Assert.Equal(0x8e2302b8u, Word(overlay, 0x0030282c));
        Assert.Equal(0x906400bcu, Word(overlay, 0x00302844));
        Assert.Equal(0x54800009u, Word(overlay, 0x00302848));
        Assert.Equal(0x8e2502bcu, Word(overlay, 0x00302850));
        Assert.Equal(0x34421000u, Word(overlay, 0x0030287c));
        Assert.Equal(0xa2640020u, Word(overlay, 0x00302880));
        Assert.Equal(0xa6620034u, Word(overlay, 0x00302888));
    }

    [Fact]
    public void Class623AuthoredInitializationRespectsBothSourceGates()
    {
        byte[] pvar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt64LittleEndian(pvar.AsSpan(0x2b8), -1);
        Assert.Equal(new Rac1Class623InitialTargetState(1, true),
            Rac1AmoeboidClass623Admission.Read(pvar));

        BinaryPrimitives.WriteInt64LittleEndian(pvar.AsSpan(0x2b8), 984);
        Assert.Equal(new Rac1Class623InitialTargetState(15, false),
            Rac1AmoeboidClass623Admission.Read(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2b4), 1);
        Assert.Equal(new Rac1Class623InitialTargetState(12, false),
            Rac1AmoeboidClass623Admission.Read(pvar));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AmoeboidClass623Admission.Read(new byte[0x2b8]));
    }

    [SkippableFact]
    public void RetailRilgarClass623InitialTargetEligibilityMatchesStateZeroBranches()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var sources = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances
            .Where(x => x.OClass == 623).ToArray();

        Assert.Equal(56, sources.Length);
        Assert.All(sources, source =>
        {
            Assert.NotNull(source.PVar);
            Assert.Equal(Rac1AmoeboidClass623Admission.PVarSize, source.PVar!.Length);
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(source.PVar.AsSpan(0x2b4)));
        });
        var eligible = sources.Where(x =>
            Rac1AmoeboidClass623Admission.Read(x.PVar!).HasTargetableFlag).ToArray();
        Assert.Equal(12, eligible.Length);
        Assert.Equal(44, sources.Count(x =>
            Rac1AmoeboidClass623Admission.Read(x.PVar!).NativeState == 15));
        Assert.Equal(new[] { 1002, 1003, 1004, 1005, 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1016 },
            eligible.Select(x => x.Index).ToArray());
        Assert.Equal(new[] { 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1016 },
            eligible.Where(x => Rac1AmoeboidAuthored.ReadGroup(x.RawRecord) == 101)
                .Select(x => x.Index).ToArray());
        Assert.Equal(15, Rac1AmoeboidClass623Admission.Read(
            sources.Single(x => x.Index == 984).PVar!).NativeState);
        Assert.DoesNotContain(eligible, x => Rac1AmoeboidAuthored.ReadGroup(x.RawRecord) is 2 or 74 or 77);
    }

    [Fact]
    public void WaitingClass623RequiresMatchingNativeControllerOrVolumeSignal()
    {
        byte[] pvar = new byte[Rac1AmoeboidClass623Admission.PVarSize];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2b8), 1071);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2bc), -1);
        var waiting = Rac1AmoeboidClass623Admission.Read(pvar);
        var links = Rac1AmoeboidClass623Admission.ReadActivationLinks(pvar);
        Assert.Equal(new Rac1Class623ActivationLinks(1071, -1), links);
        Assert.Equal(new Rac1Class623InitialTargetState(15, false), waiting);
        Assert.Equal(waiting, Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, null, true));
        Assert.Equal(waiting, Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, 0, null));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true),
            Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, 1, null));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true),
            Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, 2, null));

        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2bc), 58);
        links = Rac1AmoeboidClass623Admission.ReadActivationLinks(pvar);
        Assert.Equal(waiting, Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, null, false));
        Assert.Equal(new Rac1Class623InitialTargetState(1, true),
            Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, null, true));
        var admitted = Rac1AmoeboidClass623Admission.AdvanceWaiting(waiting, links, null, true);
        Assert.Equal(admitted,
            Rac1AmoeboidClass623Admission.AdvanceWaiting(admitted, links, 0, false));
        Assert.Equal(new Rac1Class623InitialTargetState(12, false),
            Rac1AmoeboidClass623Admission.AdvanceWaiting(new(12, false), links, 2, true));

        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x2b8), -2);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AmoeboidClass623Admission.ReadActivationLinks(pvar));
    }

    [SkippableFact]
    public void RetailClass623WaitingLinksResolveToClass830AndAuthoredTriggerVolumes()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var mobys = Rac1Instances.Parse(gameplay).MobyInstances;
        var volumes = Rac1ClipVolumes.Read(gameplay);
        var waiting = mobys.Where(x => x.OClass == 623 &&
            Rac1AmoeboidClass623Admission.Read(x.PVar!).NativeState == 15).ToArray();
        Assert.Equal(44, waiting.Length);
        Assert.Equal(25, mobys.Count(x => x.OClass == 830));
        foreach (var target in waiting)
        {
            var links = Rac1AmoeboidClass623Admission.ReadActivationLinks(target.PVar!);
            Assert.InRange(links.LinkedMobyIndex, 0, mobys.Count - 1);
            Assert.Equal(830, mobys[links.LinkedMobyIndex].OClass);
            if (links.TriggerVolumeIndex >= 0)
            {
                Assert.InRange(links.TriggerVolumeIndex, 0, volumes.Count - 1);
                Assert.True(volumes[links.TriggerVolumeIndex].Contains(volumes[links.TriggerVolumeIndex].Centre));
            }
        }
        Assert.Equal(new[] { 51, 52, 58 }, waiting
            .Select(x => Rac1AmoeboidClass623Admission.ReadActivationLinks(x.PVar!).TriggerVolumeIndex)
            .Where(x => x >= 0).Distinct().OrderBy(x => x));
        Assert.Equal(9, waiting.Count(x =>
            Rac1AmoeboidClass623Admission.ReadActivationLinks(x.PVar!).TriggerVolumeIndex >= 0));
    }

    [SkippableFact]
    public void RetailClass830WritesActivationByteAndClass623ChecksBothGates()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var overlay = Rac1ExecutableOverlay.Read(reader,
            Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5));
        static uint Word(Rac1ExecutableOverlay source, int address) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Read(address, 4));

        Assert.Equal(0xa2040020u, Word(overlay, 0x0030c69c)); // class-830 state 2
        Assert.Equal(0xa20400bcu, Word(overlay, 0x0030c6a8)); // immediate state-2 signal 2
        Assert.Equal(0x24040001u, Word(overlay, 0x0030c868)); // state-1 transition signal 1
        Assert.Equal(0xa20400bcu, Word(overlay, 0x0030c878)); // write completion byte 1
        Assert.Equal(0xa2030020u, Word(overlay, 0x0030c880)); // transition to state 2
        Assert.Equal(0x906400bcu, Word(overlay, 0x00302844)); // class-623 link signal
        Assert.Equal(0x54800009u, Word(overlay, 0x00302848)); // activate if nonzero
        Assert.Equal(0x8e2502bcu, Word(overlay, 0x00302850)); // alternate volume index
        Assert.Equal(0x0c0a271au, Word(overlay, 0x0030285c)); // native volume predicate
        Assert.Equal(0x34421000u, Word(overlay, 0x0030287c)); // restore 0x1000 flag
        Assert.Equal(0xa2640020u, Word(overlay, 0x00302880)); // state 1
        // Native volume query uses its 0x80-stride source cuboid table.
        Assert.Equal(0x000511c0u, Word(overlay, 0x00289c84));
    }

    [SkippableFact]
    public void RetailRilgarAmoeboidTargetGroupsContainAuthenticClass623Members()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 5);
        var instances = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level)).MobyInstances;
        var candidateGroups = instances.Where(x => x.OClass == 623)
            .GroupBy(x => Rac1AmoeboidAuthored.ReadGroup(x.RawRecord))
            .ToDictionary(x => x.Key, x => x.Count());
        var amoeboids = instances.Where(x => Rac1AmoeboidAuthored.IsFamily(x.OClass)).ToArray();
        int targets = 0;
        foreach (var source in amoeboids)
        {
            var facts = Rac1AmoeboidAuthored.Read(source.OClass, source.PVar!);
            if (facts.TargetGroup < 0) continue;
            Assert.True(candidateGroups.GetValueOrDefault(facts.TargetGroup) > 0,
                $"Rilgar Amoeboid i{source.Index} refers to unavailable group {facts.TargetGroup}");
            targets++;
        }
        Assert.Equal(56, candidateGroups.Values.Sum());
        Assert.Equal(52, targets);
        Assert.Equal(new[] { 2, 74, 77, 101 },
            amoeboids.Select(x => Rac1AmoeboidAuthored.Read(x.OClass, x.PVar!).TargetGroup)
                .Where(g => g >= 0).Distinct().OrderBy(g => g).ToArray());
    }
}
