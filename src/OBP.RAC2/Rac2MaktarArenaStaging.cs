using OBP.Core;
using OBP.Runtime;

namespace OBP.RAC2;

/// <summary>
/// Optional OBP-authored arena test entry, not a native campaign spawn.
/// The height and floor footprint were measured from the authorised LEVEL2
/// collision. The caller must provide the actual current source world.
/// </summary>
public static class Rac2MaktarArenaStaging
{
    public const int NativeLevel = 2;
    public const double FloorHeight = 500.0;
    public const double CentreX = 500.0;
    public const double CentreZ = 600.0;

    public static RuntimeSpawn CreateValidatedEntry(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Game != ObpSourceGame.Rac2 || world.LevelId != NativeLevel ||
            !string.Equals(world.BuildId, Rac2Authority.Primary.BuildId, StringComparison.Ordinal))
            throw new InvalidDataException("The Maktar arena test entry requires the native R&C2 LEVEL2 world.");

        foreach (var point in new[] { (500d, 600d), (505d, 600d), (505d, 607d) })
        {
            if (!HasFloorAt(world, point.Item1, point.Item2))
                throw new InvalidDataException("The native Maktar arena floor is missing collision at the test entry.");
        }

        // Slightly above the verified native floor, so the ordinary shared
        // player ground snap takes control. This is not a GC spawn point.
        return new RuntimeSpawn(505, 501.5, 607, 0);
    }

    public static bool HasFloorAt(RuntimeWorld world, double x, double z)
    {
        foreach (var blob in world.CollisionMeshes)
        {
            var positions = blob.Positions;
            var indices = blob.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i] * 3, b = indices[i + 1] * 3, c = indices[i + 2] * 3;
                double ax = positions[a], az = positions[a + 2];
                double bx = positions[b], bz = positions[b + 2];
                double cx = positions[c], cz = positions[c + 2];
                double det = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                if (Math.Abs(det) < 1e-9) continue;
                double u = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / det;
                double v = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / det;
                if (u < -1e-5 || v < -1e-5 || u + v > 1.00001) continue;
                double y = positions[c + 1] + u * (positions[a + 1] - positions[c + 1]) +
                           v * (positions[b + 1] - positions[c + 1]);
                if (y < FloorHeight - 1 || y > FloorHeight + 1) continue;
                double uy = positions[b + 1] - positions[a + 1];
                double vy = positions[c + 1] - positions[a + 1];
                double ux = bx - ax, uz = bz - az, vx = cx - ax, vz = cz - az;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double normalLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (normalLength > 1e-9 && Math.Abs(ny) / normalLength >= .85)
                    return true;
            }
        }
        return false;
    }
}
