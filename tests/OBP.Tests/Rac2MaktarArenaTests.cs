using OBP.Core;
using OBP.IO;
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
