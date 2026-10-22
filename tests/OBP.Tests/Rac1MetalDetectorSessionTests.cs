using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1MetalDetectorSessionTests
{
    [Fact]
    public void TargetSelectionHonoursAuthoredPolygonOrClipVolumeAndNearestDistance()
    {
        Vector3[] polygon = [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)];
        var clip = new Rac1ClipVolume(0, new(20, 0, 0), new(4, 0, 0), new(0, 4, 0), new(0, 0, 3));
        var session = new Rac1MetalDetectorSession([polygon], [clip]);
        session.Register(Placement(11, 2, 0, 0), new(0, -1, 50));
        session.Register(Placement(12, 19, 0, 0), new(-1, 0, 50));
        session.Register(Placement(13, 3, 0, 0), new(-1, -1, 10));
        Assert.Equal(3, session.RegisteredCount);
        var nearest = Assert.IsType<Rac1MetalDetectorCandidate>(session.FindNearest(new(1, 0, 0)));
        Assert.Equal(11, nearest.InstanceIndex);
        Assert.Equal(1, nearest.Distance, 5);
        Assert.Equal(12, session.FindNearest(new Vector3(19, 0, 0))!.InstanceIndex);
        Assert.Equal(13, session.FindNearest(new Vector3(50, 0, 0))!.InstanceIndex);
    }

    [Fact]
    public void NativeDetectorRequiresHeldInputAgeAndStrictOneUnitRange()
    {
        var session = new Rac1MetalDetectorSession([], []);
        session.Register(Placement(42, 0, 0, 0), new(-1, -1, 50));
        var selected = session.FindNearest(new(.5f, 0, 0));
        Assert.NotNull(selected);
        Assert.Null(session.TryExtract(selected, false, 20));
        Assert.Null(session.TryExtract(selected, true, 15));
        Assert.Null(session.TryExtract(session.FindNearest(new(1, 0, 0)), true, 20));
        Assert.Equal(50, session.Remaining(42));
        var extraction = Assert.IsType<Rac1BuriedBoltExtraction>(session.TryExtract(selected, true, 16));
        Assert.Equal(50, extraction.AvailableBolts);
        Assert.Equal(10, extraction.FiveBoltBursts);
        Assert.Equal(0, session.Remaining(42));
        Assert.Null(session.FindNearest(Vector3.Zero));
        Assert.Null(session.TryExtract(selected, true, 45));
    }

    [Fact]
    public void SourcePackedNibbleHistoryPersistsSiteDepletionAcrossPlanetReloads()
    {
        var progress = new Rac1BuriedBoltProgress();
        var first = new Rac1MetalDetectorSession([], [], progress, levelId: 2);
        first.Register(Placement(10, 0, 0, 0), new(-1, -1, 50));
        first.Register(Placement(11, 1, 0, 0), new(-1, -1, 10));
        var mine = first.TryExtract(first.FindNearest(Vector3.Zero), true, 16);
        Assert.Equal(10, mine!.FiveBoltBursts);
        Assert.Equal(10, progress.Read(2, 0));
        Assert.Equal(0, progress.Read(2, 1));
        Assert.Equal(0xa0, progress.Snapshot()[2 * 16]);
        var revisit = new Rac1MetalDetectorSession([], [], progress, levelId: 2);
        revisit.Register(Placement(10, 0, 0, 0), new(-1, -1, 50));
        revisit.Register(Placement(11, 1, 0, 0), new(-1, -1, 10));
        Assert.Equal(1, revisit.ActiveCount);
        Assert.Equal(11, revisit.FindNearest(Vector3.Zero)!.InstanceIndex);
        var extraction = revisit.TryExtract(revisit.FindNearest(new(.5f, 0, 0)), true, 16);
        Assert.Equal(2, extraction!.FiveBoltBursts);
        Assert.Equal(0xa2, progress.Snapshot()[2 * 16]);
        Assert.Null(revisit.FindNearest(Vector3.Zero));
        for (int i = 0; i < 20; i++) progress.Increment(2, 1);
        Assert.Equal(15, progress.Read(2, 1));
        Assert.Equal(10, progress.Read(2, 0));
        Assert.Equal(0xaf, progress.Snapshot()[2 * 16]);
        Assert.Equal(0, progress.Read(3, 0));
    }

    [Fact]
    public void NativeHistoryRoundTripsThroughCurrentCampaignSaveAndOldSavesDefaultZero()
    {
        var original = new Rac1BuriedBoltProgress();
        for (int i = 0; i < 7; i++) original.Increment(4, 3);
        var save = Rac1CampaignSavePolicy.Capture(
            new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningVeldinWitness(),
            buriedBoltHistory: original);
        var restored = Rac1CampaignSavePolicy.RestoreOrDefault(save);
        Assert.Equal(7, Rac1BuriedBoltProgress.Restore(restored.BuriedBoltHistory).Read(4, 3));
        Assert.Equal(320, restored.BuriedBoltHistory.Length);
        var legacy = Rac1CampaignSavePolicy.RestoreOrDefault(save with { BuriedBoltHistory = [] });
        Assert.Equal(0, Rac1BuriedBoltProgress.Restore(legacy.BuriedBoltHistory).Read(4, 3));
        Assert.Throws<InvalidDataException>(() =>
            Rac1CampaignSavePolicy.RestoreOrDefault(save with { BuriedBoltHistory = [1, 2, 3] }));
    }

    [Fact]
    public void RejectsUnavailableNativePathsVolumesAndDuplicateTargets()
    {
        var session = new Rac1MetalDetectorSession([], []);
        Assert.Throws<InvalidDataException>(() =>
            session.Register(Placement(1, 0, 0, 0), new(0, -1, 50)));
        Assert.Throws<InvalidDataException>(() =>
            session.Register(Placement(2, 0, 0, 0), new(-1, 0, 50)));
        session.Register(Placement(2, 0, 0, 0), new(-1, -1, 10));
        Assert.Throws<InvalidOperationException>(() =>
            session.Register(Placement(2, 0, 0, 0), new(-1, -1, 10)));
        Assert.Equal(1, session.ActiveCount);
    }

    [SkippableFact]
    public void EveryRetailBuriedTargetHasValidSourcePathAndClipVolume()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0, polygon = 0, volume = 0, plain = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var paths = Rac1Paths.Read(gameplay);
            var volumes = Rac1ClipVolumes.Read(gameplay);
            var session = new Rac1MetalDetectorSession(paths, volumes);
            foreach (var source in Rac1Instances.Parse(gameplay).MobyInstances.Where(m => m.OClass == 605))
            {
                var pvar = source.PVar!;
                var authored = new Rac1MetalDetectorTargetAuthored(
                    BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x10)),
                    BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x14)),
                    BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x18)));
                session.Register(source, authored);
                if (authored.PathIndex >= 0) polygon++;
                if (authored.VolumeIndex >= 0)
                {
                    volume++;
                    var pos = source.Position;
                    _ = volumes[authored.VolumeIndex].Contains(new(pos.X, pos.Y, pos.Z));
                }
                if (authored.PathIndex < 0 && authored.VolumeIndex < 0) plain++;
                total++;
            }
            Assert.Equal(Rac1Instances.Parse(gameplay).MobyInstances.Count(m => m.OClass == 605),
                session.RegisteredCount);
        }
        Assert.Equal(358, total);
        Assert.Equal(46, polygon);
        Assert.Equal(95, volume);
        Assert.InRange(plain, 200, 260);
    }

    private static Rac1Instances.MobyInstance Placement(int id, float x, float y, float z) =>
        new(id, 605, 1, (x, y, z), (0, 0, 0), null, 0, new byte[0x78], new byte[0x20]);
}
