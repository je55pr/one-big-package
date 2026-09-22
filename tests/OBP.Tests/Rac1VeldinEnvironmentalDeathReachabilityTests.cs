using OBP.IO;
using Godot;
using OBP.Godot;
using OBP.RAC1;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1VeldinEnvironmentalDeathReachabilityTests
{
    [SkippableFact]
    public void RetailVeldin_AuthoredStartHasReachableCollisionEdgeAboveRecoveredDeathPlane()
    {
        string? iso = System.Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac1WorldImport.Build(reader, 0);

        RuntimeSpawn start = Assert.IsType<RuntimeSpawn>(world.PlayerStart);
        RuntimeEnvironment environment = Assert.IsType<RuntimeEnvironment>(world.Environment);
        RuntimeCollisionBlob collision = Assert.Single(world.CollisionMeshes);
        Assert.Equal(27f, environment.DeathHeight);
        Assert.Equal(31.43d, start.Y, 2);

        var sceneStart = RuntimeWorldScene.ToScene(start.X, start.Y, start.Z);
        float sceneYaw = RuntimeWorldScene.ToSceneYaw(start.Yaw);
        Vector3 sceneForward = Vector3.Forward.Rotated(Vector3.Up, sceneYaw).Normalized();
        double runtimeForwardX = -sceneForward.X;
        double runtimeForwardZ = sceneForward.Z;
        var sceneForwardPoint = RuntimeWorldScene.ToScene(
            start.X + runtimeForwardX,
            start.Y,
            start.Z + runtimeForwardZ);

        Assert.Equal((float)-start.X, sceneStart.X, 4);
        Assert.Equal((float)start.Y, sceneStart.Y, 4);
        Assert.Equal((float)start.Z, sceneStart.Z, 4);
        Assert.InRange((sceneForwardPoint - sceneStart).DistanceTo(sceneForward), 0f, 0.0001f);

        var nearby = BuildNearbyWalkableTriangles(collision, start.X, start.Z, radius: 90d);
        double? startFloor = FloorAt(nearby, start.X, start.Z, start.Y + 6d);
        Assert.NotNull(startFloor);
        Assert.InRange(start.Y - startFloor!.Value, -0.05d, 0.35d);

        const double step = 0.75d;
        double previousFloor = startFloor.Value;
        double? edgeDistance = null;
        for (double distance = step; distance <= 30d; distance += step)
        {
            double? floor = FloorAt(
                nearby,
                start.X + runtimeForwardX * distance,
                start.Z + runtimeForwardZ * distance,
                previousFloor + 4d);
            if (!floor.HasValue)
            {
                edgeDistance = distance;
                break;
            }

            Assert.InRange(floor.Value - previousFloor, -2d, 1.25d);
            previousFloor = floor.Value;
        }

        Assert.NotNull(edgeDistance);
        Assert.InRange(edgeDistance!.Value, 9d, 18d);
        Assert.True(previousFloor > environment.DeathHeight + 1d);
    }

    private static List<ProjectedTriangle> BuildNearbyWalkableTriangles(
        RuntimeCollisionBlob collision,
        double x,
        double z,
        double radius)
    {
        var result = new List<ProjectedTriangle>();
        for (int face = 0; face < collision.Indices.Length / 3; face++)
        {
            int ia = collision.Indices[face * 3] * 3;
            int ib = collision.Indices[face * 3 + 1] * 3;
            int ic = collision.Indices[face * 3 + 2] * 3;
            var a = new Point(collision.Positions[ia], collision.Positions[ia + 1], collision.Positions[ia + 2]);
            var b = new Point(collision.Positions[ib], collision.Positions[ib + 1], collision.Positions[ib + 2]);
            var c = new Point(collision.Positions[ic], collision.Positions[ic + 1], collision.Positions[ic + 2]);

            double minX = Math.Min(a.X, Math.Min(b.X, c.X));
            double maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z));
            double maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            if (maxX < x - radius || minX > x + radius || maxZ < z - radius || minZ > z + radius)
                continue;
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            double normalLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (normalLength <= 1e-9d || Math.Abs(ny) / normalLength < 0.5d)
                continue;

            result.Add(new ProjectedTriangle(a, b, c, minX, maxX, minZ, maxZ));
        }

        return result;
    }

    private static double? FloorAt(
        IReadOnlyList<ProjectedTriangle> triangles,
        double x,
        double z,
        double rayTop)
    {
        double? best = null;
        foreach (ProjectedTriangle triangle in triangles)
        {
            if (x < triangle.MinX || x > triangle.MaxX || z < triangle.MinZ || z > triangle.MaxZ)
                continue;
            double denominator =
                (triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X) +
                (triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z);
            if (Math.Abs(denominator) <= 1e-9d)
                continue;

            double wa =
                ((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X) +
                 (triangle.C.X - triangle.B.X) * (z - triangle.C.Z)) / denominator;
            double wb =
                ((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X) +
                 (triangle.A.X - triangle.C.X) * (z - triangle.C.Z)) / denominator;
            double wc = 1d - wa - wb;
            const double epsilon = 1e-8d;
            if (wa < -epsilon || wb < -epsilon || wc < -epsilon)
                continue;

            double y = wa * triangle.A.Y + wb * triangle.B.Y + wc * triangle.C.Y;
            if (y > rayTop + 1e-6d)
                continue;
            if (best is null || y > best.Value)
                best = y;
        }

        return best;
    }
    private readonly record struct Point(double X, double Y, double Z);
    private readonly record struct ProjectedTriangle(
        Point A,
        Point B,
        Point C,
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);
}
