using OBP.Core;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.RAC2;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>
/// Source-backed floor evidence for the isolated Galactic Gladiators stage.
/// The arena start is an explicitly opt-in OBP test fixture, not a GC grant.
/// </summary>
public sealed class Rac2MaktarArenaTests
{
    [SkippableFact]
    public void Level2_StagedCombatFloorIsNativeCollisionAndVisibleGeometry()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(path) || !File.Exists(path), "Authorised Going Commando ISO not attached");
        using var reader = new FileRandomAccessReader(path!);
        var world = Rac2WorldImport.Build(reader, Rac2MaktarArenaStaging.NativeLevel);
        Assert.Equal(ObpSourceGame.Rac2, world.Game.SourceGame);
        Assert.Equal(2, world.LevelId);
        Assert.True(world.TotalCollisionTriangles > 200_000);
        Assert.True(world.TotalRenderTriangles > 1_000_000);

        var spawn = Rac2MaktarArenaStaging.CreateValidatedEntry(world);
        Assert.Equal(505, spawn.X);
        Assert.InRange(spawn.Y, 500.0, 503.0);
        Assert.Equal(607, spawn.Z);
        Assert.True(Rac2MaktarArenaStaging.HasFloorAt(world, 500, 600));
        Assert.True(Rac2MaktarArenaStaging.HasFloorAt(world, 505, 607));
        Assert.False(Rac2MaktarArenaStaging.HasFloorAt(world, 315, 665));

        // Native meshes near the raised stage include the authored floor,
        // machinery and audience environment without generated stand-ins.
        var localTypes = world.Meshes.Where(mesh => IsNearArena(mesh))
            .Select(mesh => mesh.AssetKind).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("tfrag", localTypes);
        Assert.Contains("tie", localTypes);
        Assert.Contains("moby", localTypes);
    }

    [Fact]
    public void DispenserStaticPresentationIsScopedToMaktar()
    {
        Assert.False(Rac2MaktarArenaPresentation.AdmitStaticMoby(2, 2581));
        Assert.True(Rac2MaktarArenaPresentation.AdmitStaticMoby(2, 2580));
        Assert.True(Rac2MaktarArenaPresentation.AdmitStaticMoby(1, 2581));
        Assert.True(Rac2MaktarArenaPresentation.AdmitStaticMoby(0, 2581));
    }

    [SkippableFact]
    public void Level2_DispenserPlacementsAndMovingSourceModelAreRetained()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(path) || !File.Exists(path), "Authorised Going Commando ISO not attached");
        using var reader = new FileRandomAccessReader(path!);
        var iso = Iso9660Filesystem.Open(reader);
        var wad = iso.OpenFile("/G/LEVEL2.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var gameplay = Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2));
        var instances = gameplay.MobyInstances.Where(m => m.OClass == Rac2MaktarArenaPresentation.DispenserClass).ToArray();
        Assert.Equal(6, instances.Length);
        Assert.All(instances, m => Assert.InRange(m.Position.Z, 503, 504));
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var nativeClass = Rac2MobyClasses.Read(core, 2)[Rac2MaktarArenaPresentation.DispenserClass];
        Assert.Equal(5, nativeClass.Joints.Count);
        Assert.Equal(1188, nativeClass.Mesh.Indices.Length / 3);
        Assert.Equal(4, nativeClass.Sequences.Count);
        Assert.All(nativeClass.Sequences, sequence => Assert.NotEmpty(sequence.Frames));
    }

    [Fact]
    public void WrongWorldAndMissingCollisionCannotUseArenaTestEntry()
    {
        var empty = new RuntimeWorld(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId,
            Rac2MaktarArenaStaging.NativeLevel, null, null, [], [], 0, [],
            new OBP.Core.Math.ObpBounds(OBP.Core.Math.Vec3.Zero, OBP.Core.Math.Vec3.Zero), null, null);
        Assert.Throws<InvalidDataException>(() => Rac2MaktarArenaStaging.CreateValidatedEntry(empty));
        Assert.Throws<InvalidDataException>(() => Rac2MaktarArenaStaging.CreateValidatedEntry(empty with { LevelId = 1 }));
        Assert.Throws<InvalidDataException>(() => Rac2MaktarArenaStaging.CreateValidatedEntry(empty with { BuildId = "unrelated" }));
    }

    private static bool IsNearArena(RuntimeMesh mesh)
    {
        // Source world is Y-up. Source objects remain unchanged.
        double[] p = mesh.Positions;
        for (int i = 0; i + 2 < p.Length; i += 3)
            if (p[i] >= 455 && p[i] <= 545 && p[i + 1] >= 495 &&
                p[i + 1] <= 525 && p[i + 2] >= 565 && p[i + 2] <= 635)
                return true;
        return false;
    }
}
