using OBP.Core;
using OBP.Runtime;

namespace OBP.RAC1.Player;

/// <summary>
/// Finds authored class-zero water-like collision surfaces in Novalis, preserving
/// their local heights and footprint. This is NOT a recovered underwater volume:
/// it cannot establish depth, flow or the retail water action admission rules.
/// </summary>
public sealed class Rac1NovalisWaterSurfaces
{
    private const double CellSize = 8d;
    private readonly Dictionary<(int X, int Z), List<Triangle>> _grid = [];
    private int _count;

    private readonly record struct Point(double X, double Y, double Z);
    private readonly record struct Triangle(Point A, Point B, Point C, double SignedArea,
        double MinX, double MaxX, double MinZ, double MaxZ);

    public int SurfaceTriangleCount => _count;

    public static Rac1NovalisWaterSurfaces FromWorld(RuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Game != ObpSourceGame.Rac1 || world.LevelId != 1)
            throw new ArgumentException("Novalis water lookup requires the R&C1 LEVEL1 world.", nameof(world));
        var result = new Rac1NovalisWaterSurfaces();
        foreach (var blob in world.CollisionMeshes)
        {
            for (int face = 0; face < blob.Triangles; face++)
            {
                int raw = blob.TriangleMaterialIds[face];
                if (raw is < 0 or > 255 ||
                    Rac1CollisionFaceSemantics.Decode(raw).SurfaceClass != 0)
                    continue;
                Point Vertex(int i)
                {
                    int at = blob.Indices[face * 3 + i] * 3;
                    return new(blob.Positions[at], blob.Positions[at + 1], blob.Positions[at + 2]);
                }
                result.Add(Vertex(0), Vertex(1), Vertex(2));
            }
        }
        return result;
    }

    private void Add(Point a, Point b, Point c)
    {
        double signed = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
        double normalHorizontal = Math.Sqrt(
            Math.Pow((b.Y - a.Y) * (c.Z - a.Z) - (c.Y - a.Y) * (b.Z - a.Z), 2) +
            Math.Pow((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y), 2));
        // Vertical and near-vertical class-zero walls cannot define water height.
        if (Math.Abs(signed) < 1e-8 || Math.Abs(signed) < normalHorizontal)
            return;
        double minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
        double minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
        var triangle = new Triangle(a, b, c, signed, minX, maxX, minZ, maxZ);
        _count++;
        for (int x = Cell(minX); x <= Cell(maxX); x++)
            for (int z = Cell(minZ); z <= Cell(maxZ); z++)
            {
                if (!_grid.TryGetValue((x, z), out var bucket))
                    _grid.Add((x, z), bucket = []);
                bucket.Add(triangle);
            }
    }

    public bool TrySample(double x, double z, out double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x), "Water lookup requires finite coordinates.");
        height = 0;
        if (!_grid.TryGetValue((Cell(x), Cell(z)), out var bucket)) return false;
        bool found = false;
        foreach (var tri in bucket)
        {
            if (x < tri.MinX - 1e-5 || x > tri.MaxX + 1e-5 ||
                z < tri.MinZ - 1e-5 || z > tri.MaxZ + 1e-5)
                continue;
            // Barycentric interpolation within the actual source triangle,
            // avoiding a fabricated planet-wide height or bounding rectangle.
            double b = ((x - tri.A.X) * (tri.C.Z - tri.A.Z) -
                         (tri.C.X - tri.A.X) * (z - tri.A.Z)) / tri.SignedArea;
            double c = ((tri.B.X - tri.A.X) * (z - tri.A.Z) -
                         (x - tri.A.X) * (tri.B.Z - tri.A.Z)) / tri.SignedArea;
            double a = 1d - b - c;
            if (a < -1e-5 || b < -1e-5 || c < -1e-5) continue;
            double y = a * tri.A.Y + b * tri.B.Y + c * tri.C.Y;
            if (!found || y > height) { found = true; height = y; }
        }
        return found;
    }

    private static int Cell(double coordinate) => checked((int)Math.Floor(coordinate / CellSize));
}
