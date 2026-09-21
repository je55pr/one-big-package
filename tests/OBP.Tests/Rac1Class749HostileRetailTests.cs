using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1Class749HostileRetailTests
{
    [SkippableFact]
    public void VeldinCarriesSixteenExactClass749Pvars()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 0);
        var hostiles = world.DynamicObjects!
            .Where(o => o.NativeClassId == Rac1Class749Hostile.NativeClassId)
            .OrderBy(o => o.InstanceIndex)
            .ToArray();

        Assert.Equal(16, hostiles.Length);
        Assert.All(hostiles, hostile =>
        {
            Assert.Null(hostile.NativeUid);
            var authored = Assert.IsType<Rac1Class749AuthoredState>(
                Rac1Class749Hostile.ReadAuthored(hostile));
            Assert.Equal(hostile.InstanceIndex, authored.Key.InstanceIndex);
            Assert.Equal(Rac1Class749Hostile.PVarSize, authored.PVarSize);
        });
    }

    [SkippableFact]
    public void AllNativeLevelsRetainAuthoredClass749PlacementsWithoutExpandingRuntimeWitness()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);

        for (int levelId = 0; levelId < 19; levelId++)
        {
            var world = Rac1WorldImport.Build(reader, levelId);
            var class749 = world.DynamicObjects!
                .Where(o => o.NativeClassId == Rac1Class749Hostile.NativeClassId)
                .OrderBy(o => o.InstanceIndex)
                .ToArray();
            int expectedCount = levelId switch
            {
                0 => 16,
                18 => 90,
                _ => 0,
            };
            Assert.Equal(expectedCount, class749.Length);

            foreach (var source in class749)
            {
                var authored = Assert.IsType<Rac1Class749AuthoredState>(
                    Rac1Class749Hostile.ReadAuthored(source));
                Assert.Equal(1f, authored.Health);
                Assert.Equal(0, authored.StatusSentinel);
                Assert.Equal(Rac1Class749Hostile.PVarSize, authored.PVarSize);
            }

            var runtimeWitnesses = class749
                .Where(source => Rac1Class749Hostile.IsRetainedRuntimeWitness(levelId, source))
                .ToArray();
            int expectedRuntimeWitnessCount =
                levelId == Rac1Class749Hostile.RetainedRuntimeWitnessLevelId ? 1 : 0;
            Assert.Equal(expectedRuntimeWitnessCount, runtimeWitnesses.Length);
            if (runtimeWitnesses.Length == 1)
                Assert.Equal(
                    Rac1Class749Hostile.RetainedRuntimeWitnessInstanceIndex,
                    runtimeWitnesses[0].InstanceIndex);
        }
    }

    [SkippableFact]
    public void VeldinInstance149ReplaysRepresentativeWitness()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, Rac1Class749Hostile.RetainedRuntimeWitnessLevelId);
        var specimen = Assert.Single(
            world.DynamicObjects!,
            o => o.InstanceIndex == Rac1Class749Hostile.RetainedRuntimeWitnessInstanceIndex);
        Assert.Equal(Rac1Class749Hostile.NativeClassId, specimen.NativeClassId);
        Assert.True(Rac1Class749Hostile.IsRetainedRuntimeWitness(world.LevelId, specimen));

        var authored = Assert.IsType<Rac1Class749AuthoredState>(
            Rac1Class749Hostile.ReadAuthored(specimen));
        Assert.Equal(1f, authored.Health);
        Assert.Equal(0x280, authored.PVarSize);

        var session = new Rac1Class749HostileSession();
        var registered = session.RegisterRepresentative(
            specimen, RuntimeEntityState.FromAuthored(specimen));
        Assert.Equal(Rac1Class749Hostile.TargetSearchNativeState, registered.NativeState);
        Assert.Equal(RuntimeEntityPresence.Active, registered.EntityState.Presentation.Presence);
    }
}
