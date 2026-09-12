using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1BoltCrateRetailTests
{
    [SkippableFact]
    public void VeldinClass500CarriesRetailUidRewardAndPvarAuthority()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 0);
        var objects = Assert.IsAssignableFrom<IReadOnlyList<RuntimeDynamicObject>>(world.DynamicObjects);
        var crates = objects.Where(o => o.NativeClassId == 500).OrderBy(o => o.InstanceIndex).ToArray();

        Assert.Equal(103, crates.Length);
        Assert.All(crates, crate =>
        {
            Assert.NotNull(crate.NativeUid);
            var authored = Assert.IsType<Rac1BoltCrateAuthoredState>(Rac1BoltCrate.ReadAuthored(crate));
            Assert.Equal(crate.NativeUid, authored.Uid);
            Assert.Equal(0x100, authored.PVarSize);
            Assert.Contains(authored.RewardCentre, new[] { 10, 15 });
        });

        Assert.Equal(73, crates.Count(c => Rac1BoltCrate.ReadAuthored(c)!.RewardCentre == 10));
        Assert.Equal(30, crates.Count(c => Rac1BoltCrate.ReadAuthored(c)!.RewardCentre == 15));
    }

    [SkippableFact]
    public void VeldinSpecimenReplaysRepresentativeBreakAndCollection()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 0);
        var specimen = Assert.Single(world.DynamicObjects!, o => o.InstanceIndex == 89);
        Assert.Equal(500, specimen.NativeClassId);

        var authored = Assert.IsType<Rac1BoltCrateAuthoredState>(Rac1BoltCrate.ReadAuthored(specimen));
        Assert.Equal(121, authored.Uid);
        Assert.Equal(10, authored.RewardCentre);
        Assert.Equal(new Rac1BoltRewardRange(10, 3, 7, 13), Rac1BoltCrate.RewardRange(10));

        var session = new Rac1BoltCrateSession();
        var result = Assert.IsType<Rac1BoltCrateBreakResult>(session.ApplyDamage(
            specimen, RuntimeEntityState.FromAuthored(specimen), nativeDamage: 1, selectedTotal: 7));
        Assert.Equal(new[] { 5, 1, 1 }, result.Pickups.Select(p => p.Value));
        Assert.Equal(RuntimeEntityPresence.Inactive, result.EntityState.Presentation.Presence);
        foreach (var pickup in result.Pickups) session.CollectPickup(pickup.PickupId);
        Assert.Equal(7, session.CollectedBolts);
    }

    [SkippableFact]
    public void UnprovenMobyClassesDoNotGainGenericUidSemantics()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 0);
        Assert.All(world.DynamicObjects!.Where(o => o.NativeClassId != 500), o => Assert.Null(o.NativeUid));
    }
}
