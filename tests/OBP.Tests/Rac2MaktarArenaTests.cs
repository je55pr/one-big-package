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
        Assert.All(instances, m =>
        {
            Assert.InRange(m.Position.Z, 503, 504);
            var facing = Rac2MaktarDispenserMotion.InwardPreviewRotation(
                (m.Rotation.X, m.Rotation.Y, m.Rotation.Z));
            double dx = 500 - m.Position.X, dy = 601 - m.Position.Y;
            double dot = (-Math.Sin(facing.Z) * dx + Math.Cos(facing.Z) * dy) /
                Math.Sqrt(dx * dx + dy * dy);
            Assert.InRange(dot, .99, 1.000001);
        });
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var nativeClass = Rac2MobyClasses.Read(core, 2)[Rac2MaktarArenaPresentation.DispenserClass];
        Assert.Equal(5, nativeClass.Joints.Count);
        Assert.Equal(1188, nativeClass.Mesh.Indices.Length / 3);
        Assert.Equal(4, nativeClass.Sequences.Count);
        Assert.All(nativeClass.Sequences, sequence => Assert.NotEmpty(sequence.Frames));
    }

    [SkippableFact]
    public void NativeDispenserPreviewUsesIndependentSourceFramesWithoutChangingTheWorld()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(path) || !File.Exists(path), "Authorised Going Commando ISO not attached");
        using var reader = new FileRandomAccessReader(path!);
        var world = Rac2WorldImport.Build(reader, 2);
        var originalStatic = world.TotalRenderTriangles;
        var originalCollision = world.TotalCollisionTriangles;
        var priorFrames = world.AnimatedMeshes?.Count ?? 0;

        var preview = Rac2WorldImport.WithMaktarDispenserPreview(reader, world, 394, animate: true);
        var surfaces = (preview.AnimatedMeshes ?? []).Skip(priorFrames).ToArray();
        Assert.NotEmpty(surfaces);
        Assert.All(surfaces, surface =>
        {
            Assert.StartsWith("maktar-dispenser-394-", surface.Name);
            Assert.Equal("moby", surface.AssetKind);
            Assert.Equal(108, surface.Frames.Count);
            Assert.Equal(90f, surface.FramesPerSecond);
            Assert.Equal(surface.VertexCount * 2, surface.Uvs.Length);
            Assert.All(surface.Frames, points => Assert.Equal(surface.Frames[0].Length, points.Length));
        });
        Assert.Equal(1188, surfaces.Sum(surface => surface.TriangleCount));
        Assert.Equal(originalStatic, preview.TotalRenderTriangles);
        Assert.Equal(originalCollision, preview.TotalCollisionTriangles);
        Assert.Same(world.Meshes, preview.Meshes);
        Assert.Same(world.CollisionMeshes, preview.CollisionMeshes);
        Assert.Equal(priorFrames, world.AnimatedMeshes?.Count ?? 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac2WorldImport.WithMaktarDispenserPreview(reader, world, 404, animate: false));
    }

    [Fact]
    public void InwardPreviewYawFacesAllSixAuthoredDispenserPositionsTowardArenaCentre()
    {
        // Native placement yaw makes the model's extending +Y axis point
        // radially out; the visual-preview correction reverses only facing.
        foreach (var (x, y, yaw) in new[]
        {
            (514.02, 568.43, -2.740), (485.91, 568.45, 2.740),
            (514.09, 634.66, -.401), (485.97, 634.69, .402),
            (464.02, 601.79, 1.566), (535.97, 601.32, -1.576),
        })
        {
            var transformed = Rac2MaktarDispenserMotion.InwardPreviewRotation((0, 0, yaw));
            double fx = -Math.Sin(transformed.Z), fy = Math.Cos(transformed.Z);
            double dx = 500 - x, dy = 601 - y;
            double dot = (fx * dx + fy * dy) / Math.Sqrt(dx * dx + dy * dy);
            Assert.InRange(dot, .99, 1.000001);
            Assert.Equal(yaw + Math.PI, transformed.Z, 8);
        }
    }

    [Fact]
    public void SmoothedPreviewKeepsAllOriginalKeysAndHasContinuousSeams()
    {
        var keys = new double[][]
        {
            [0, 1, 2],
            [12, 13, 14],
            [24, 25, 26],
        };
        var preview = Rac2MaktarDispenserMotion.SmoothExtendRetract(keys, 6);
        Assert.Equal(24, preview.Count);
        Assert.Equal(keys[0], preview[0]);
        Assert.Equal(keys[1], preview[6]);
        Assert.Equal(keys[2], preview[12]);
        Assert.Equal(keys[1], preview[18]);
        Assert.Equal(new double[] { 2, 3, 4 }, preview[1]);
        Assert.Equal(new double[] { 2, 3, 4 }, preview[^1]);
        Assert.Throws<InvalidDataException>(() =>
            Rac2MaktarDispenserMotion.SmoothExtendRetract([[0, 0, 0], [double.NaN, 0, 0]]));
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
