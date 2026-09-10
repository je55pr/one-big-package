using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1VeldinSpawnTests
{
    [SkippableFact]
    public void RetailVeldin_ShipIsOutsideStaticPlayableFootprint_ButClassZeroIsInside()
    {
        using var reader = OpenRetail();
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var settings = Rac1LevelSettings.Parse(gameplay);
        var ratchet = Assert.Single(Rac1Instances.Parse(gameplay).MobyInstances, moby => moby.OClass == 0);
        var world = Rac1WorldImport.Build(reader, 0);

        Assert.Equal(0, ratchet.Index);
        Assert.Equal(132.09f, ratchet.Position.X, 2);
        Assert.Equal(115.48f, ratchet.Position.Y, 2);
        Assert.Equal(31.43f, ratchet.Position.Z, 2);
        Assert.Equal(20f, settings.ShipPosition.X);
        Assert.Equal(20f, settings.ShipPosition.Y);
        Assert.Equal(20f, settings.ShipPosition.Z);

        var collision = Assert.Single(world.CollisionMeshes);
        Assert.False(InsideHorizontal(settings.ShipPosition.X, settings.ShipPosition.Y, collision.Positions));
        Assert.True(InsideHorizontal(ratchet.Position.X, ratchet.Position.Y, collision.Positions));

        double[] tfrags = world.Meshes.Where(mesh => mesh.AssetKind == "tfrag").SelectMany(mesh => mesh.Positions).ToArray();
        Assert.False(InsideHorizontal(settings.ShipPosition.X, settings.ShipPosition.Y, tfrags));
        Assert.True(InsideHorizontal(ratchet.Position.X, ratchet.Position.Y, tfrags));
    }

    [SkippableFact]
    public void RetailAllLevels_ClassZeroPlacementIsUniqueIndexZeroAndNeverShipPosition()
    {
        using var reader = OpenRetail();
        foreach (var level in Rac1DiscIndex.Read(reader).Levels.OrderBy(level => level.LevelId))
        {
            byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
            var ratchet = Assert.Single(Rac1Instances.Parse(gameplay).MobyInstances, moby => moby.OClass == 0);
            var ship = Rac1LevelSettings.Parse(gameplay).ShipPosition;
            double distance = Math.Sqrt(
                Math.Pow(ratchet.Position.X - ship.X, 2) +
                Math.Pow(ratchet.Position.Y - ship.Y, 2) +
                Math.Pow(ratchet.Position.Z - ship.Z, 2));

            Assert.Equal(0, ratchet.Index);
            Assert.Equal(1f, ratchet.Scale);
            Assert.True(distance > 1d, $"level {level.LevelId} class-0 placement unexpectedly coincides with ship ({distance:R})");
            if (level.LevelId == 0) Assert.True(distance > 100d);
            else Assert.True(distance < 30d, $"level {level.LevelId} ship/class-0 separation drifted to {distance:R}");
        }
    }

    [SkippableFact]
    public void RetailVeldin_PlayerStartProviderUsesAuthoredRatchetTransform()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var ratchet = Assert.Single(Rac1Instances.Parse(gameplay).MobyInstances, moby => moby.OClass == 0);
        var ship = Rac1LevelSettings.Parse(gameplay).ShipPosition;

        var start = Rac1PlayerStartProvider.Instance.Load(iso!, 0);

        Assert.Equal(0, start.NativeLevelId);
        Assert.Equal(0, start.InstanceIndex);
        Assert.Equal(ratchet.Position, start.NativePosition);
        Assert.Equal(ratchet.Rotation, start.NativeRotation);
        Assert.Equal(ratchet.Scale, start.Scale);
        Assert.NotEqual((double)ship.X, start.Transform.Matrix[12]);
        Assert.Equal((double)ratchet.Position.X, start.Transform.Matrix[12], 6);
        Assert.Equal((double)ratchet.Position.Z, start.Transform.Matrix[13], 6);
        Assert.Equal((double)ratchet.Position.Y, start.Transform.Matrix[14], 6);

        var nativePoint = Rac1Instances.TransformMobyPoint(ratchet, 1.25, -2.5, 3.75);
        var obpPoint = TransformPoint(start.Transform.Matrix, 1.25, 3.75, -2.5);
        Assert.Equal(nativePoint.X, obpPoint.X, 6);
        Assert.Equal(nativePoint.Z, obpPoint.Y, 6);
        Assert.Equal(nativePoint.Y, obpPoint.Z, 6);
    }

    private static (double X, double Y, double Z) TransformPoint(double[] matrix, double x, double y, double z) =>
        (
            matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
            matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
            matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14]);

    private static FileRandomAccessReader OpenRetail()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        return new FileRandomAccessReader(iso!);
    }

    private static bool InsideHorizontal(double x, double nativeY, IReadOnlyList<double> obpPositions)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        for (int i = 0; i < obpPositions.Count; i += 3)
        {
            minX = Math.Min(minX, obpPositions[i]);
            maxX = Math.Max(maxX, obpPositions[i]);
            minZ = Math.Min(minZ, obpPositions[i + 2]);
            maxZ = Math.Max(maxZ, obpPositions[i + 2]);
        }
        return x >= minX && x <= maxX && nativeY >= minZ && nativeY <= maxZ;
    }
}
