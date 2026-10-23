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
