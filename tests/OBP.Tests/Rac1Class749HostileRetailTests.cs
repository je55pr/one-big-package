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
    public void NativePlacementCensusKeepsRecoveredRuntimeScopeVeldinOnly()
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
                Assert.Equal(0, authored.InitialStatusSentinel);
                Assert.Equal(Rac1Class749Hostile.PVarSize, authored.PVarSize);
            }

            int recoveredRuntimePlacements = class749.Count(source =>
                Rac1Class749Hostile.IsRecoveredVeldinPlacement(levelId, source));
            Assert.Equal(levelId == Rac1Class749VeldinPopulation.LevelId ? 16 : 0, recoveredRuntimePlacements);
        }
    }

    [SkippableFact]
    public void VeldinRegistersAllSixteenWithoutInstance149Privilege()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, Rac1Class749VeldinPopulation.LevelId);
        var placements = world.DynamicObjects!
            .Where(o => Rac1Class749Hostile.IsRecoveredVeldinPlacement(world.LevelId, o))
            .OrderBy(o => o.InstanceIndex)
            .ToArray();
        Assert.Equal(16, placements.Length);

        var session = new Rac1Class749HostileSession();
        foreach (var placement in placements)
        {
            var registered = session.RegisterVeldinPlacement(
                placement, RuntimeEntityState.FromAuthored(placement));
            int expectedState =
                placement.InstanceIndex == Rac1Class749VeldinPopulation.SpecialLinkedInstanceIndex
                    ? Rac1Class749Hostile.LinkedObjectNativeState
                    : Rac1Class749Hostile.TargetSearchNativeState;
            Assert.Equal(expectedState, registered.NativeState);
            Assert.Equal(RuntimeEntityPresence.Active, registered.EntityState.Presentation.Presence);
        }

        Assert.Equal(16, session.RegisteredCount);
        Assert.Equal(
            Rac1Class749Hostile.TargetSearchNativeState,
            session.Probe(placements.Single(p => p.InstanceIndex == 149)).NativeState);
    }
}
