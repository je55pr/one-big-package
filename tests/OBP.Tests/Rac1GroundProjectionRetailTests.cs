using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1GroundProjectionRetailTests
{
    private const double RunCap = 0.09500919d;

    [SkippableFact]
    public void RetailUphillRun_MatchesAuthoredDirectionalSlopeLaw()
    {
        string? iso = System.Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac1WorldImport.Build(reader, 0);
        RuntimeCollisionBlob collision = Assert.Single(world.CollisionMeshes);

        RetailWitness[] witnesses =
        [
            new(119, 150.8675079345703, 113.49699401855469, 29.702327728271484,
                -2.0740408897399902, -0.044464111328125, -0.08074951171875, 0.023019790649414062),
            new(133, 150.2354278564453, 112.34904479980469, 29.84952735900879,
                -2.074047088623047, -0.0452423095703125, -0.0821685791015625, 0.0150909423828125),
            new(163, 148.93556213378906, 109.98822784423828, 30.705432891845703,
                -2.0740163326263428, -0.041778564453125, -0.075897216796875, 0.03898811340332031),
            new(168, 148.72532653808594, 109.60640716552734, 30.89126968383789,
                -2.074045419692993, -0.0430755615234375, -0.0782318115234375, 0.03242301940917969),
            new(179, 148.24783325195312, 108.7392349243164, 31.226972579956055,
                -2.074033737182617, -0.0431671142578125, -0.078399658203125, 0.03188323974609375),
            new(183, 148.07516479492188, 108.4256362915039, 31.354503631591797,
                -2.0740272998809814, -0.0431671142578125, -0.078399658203125, 0.03188323974609375),
        ];

        var diagnostics = new List<string>();
        double maxError = 0d;
        foreach (RetailWitness witness in witnesses)
        {
            Triangle triangle = FindFloorTriangle(
                collision,
                witness.NativeX + witness.Dx,
                witness.NativeY + witness.Dy,
                witness.NativeZ + witness.Dz + 1d);

            NativeVector normal = triangle.NativeUpNormal();
            NativeVector heading = new(
                Math.Cos(witness.Yaw),
                Math.Sin(witness.Yaw),
                0d);
            // Retail preserves horizontal yaw direction and lifts that heading
            // by the authored plane's directional slope. It does not use the
            // full 3D vector-plane projection, which would steer cross-slope.
            double risePerPlanarUnit =
                -((normal.X * heading.X) + (normal.Y * heading.Y)) / normal.Z;
            NativeVector tangent = new(heading.X, heading.Y, risePerPlanarUnit);
            tangent = tangent * (RunCap / tangent.Length());

            NativeVector retail = new(
                witness.Dx,
                witness.Dy,
                witness.Dz);
            double error = (tangent - retail).Length();
            maxError = Math.Max(maxError, error);

            diagnostics.Add(
                $"frame {witness.Frame}: error={error:R}, face={triangle.Face}, " +
                $"normal={normal}, projection={tangent}, retail={retail}");
        }

        Assert.True(
            maxError < 0.00003d,
            $"max directional-slope error={maxError:R}{Environment.NewLine}" +
            string.Join(Environment.NewLine, diagnostics));
    }

    [SkippableFact]
    public void RetailNeutralRelease_StopsOnAuthoredSupportedSlope()
    {
        string? iso = System.Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        RuntimeWorld world = Rac1WorldImport.Build(reader, 0);
        RuntimeCollisionBlob collision = Assert.Single(world.CollisionMeshes);

        // research/generated/rac1-ground-slope-stop.json: after neutral release,
        // retail holds this exact position with zero XYZ displacement for 45
        // consecutive updates while both unsupported counters remain zero.
        const double nativeX = 149.38661193847656d;
        const double nativeY = 110.80760192871094d;
        const double nativeZ = 30.387054443359375d;
        Triangle triangle = FindFloorTriangle(
            collision,
            nativeX,
            nativeY,
            nativeZ + 1d);
        NativeVector normal = triangle.NativeUpNormal();
        double slopeAngle = Math.Acos(Math.Clamp(normal.Z, -1d, 1d));

        Assert.True(
            slopeAngle > Math.PI / 36d,
            $"stationary retail witness resolved to an effectively flat face: " +
            $"face={triangle.Face}, normal={normal}, angle={slopeAngle:R}");
        Assert.True(
            slopeAngle < Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians,
            $"stationary retail witness resolved beyond the recovered support gate: " +
            $"face={triangle.Face}, normal={normal}, angle={slopeAngle:R}");
    }

    private static Triangle FindFloorTriangle(
        RuntimeCollisionBlob collision,
        double nativeX,
        double nativeY,
        double rayTopNativeZ)
    {
        Triangle? best = null;
        double bestY = double.NegativeInfinity;
        for (int face = 0; face < collision.Indices.Length / 3; face++)
        {
            Point a = Vertex(collision, collision.Indices[face * 3]);
            Point b = Vertex(collision, collision.Indices[face * 3 + 1]);
            Point c = Vertex(collision, collision.Indices[face * 3 + 2]);
            if (!TryFloorY(a, b, c, nativeX, nativeY, out double y))
                continue;
            if (y > rayTopNativeZ + 1e-6d || y <= bestY)
                continue;
            bestY = y;
            best = new Triangle(a, b, c, face);
        }

        return best ?? throw new InvalidOperationException(
            $"No authored collision under native ({nativeX:R},{nativeY:R},{rayTopNativeZ:R}).");
    }

    private static Point Vertex(RuntimeCollisionBlob collision, int index)
    {
        int i = index * 3;
        // Runtime collision axes are (native X, native Z, native Y).
        return new Point(
            collision.Positions[i],
            collision.Positions[i + 1],
            collision.Positions[i + 2]);
    }

    private static bool TryFloorY(
        Point a,
        Point b,
        Point c,
        double nativeX,
        double nativeY,
        out double nativeZ)
    {
        // Project runtime (X,Z) == native (X,Y); runtime Y is native Z.
        double denominator =
            (b.Z - c.Z) * (a.X - c.X) +
            (c.X - b.X) * (a.Z - c.Z);
        if (Math.Abs(denominator) <= 1e-10d)
        {
            nativeZ = 0d;
            return false;
        }

        double wa =
            ((b.Z - c.Z) * (nativeX - c.X) +
             (c.X - b.X) * (nativeY - c.Z)) / denominator;
        double wb =
            ((c.Z - a.Z) * (nativeX - c.X) +
             (a.X - c.X) * (nativeY - c.Z)) / denominator;
        double wc = 1d - wa - wb;
        const double epsilon = 1e-7d;
        if (wa < -epsilon || wb < -epsilon || wc < -epsilon)
        {
            nativeZ = 0d;
            return false;
        }

        nativeZ = wa * a.Y + wb * b.Y + wc * c.Y;
        return true;
    }

    private readonly record struct RetailWitness(
        int Frame,
        double NativeX,
        double NativeY,
        double NativeZ,
        double Yaw,
        double Dx,
        double Dy,
        double Dz);

    private readonly record struct Point(double X, double Y, double Z);

    private readonly record struct Triangle(Point A, Point B, Point C, int Face)
    {
        public NativeVector NativeUpNormal()
        {
            double ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z;
            double vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
            // Runtime normal axes -> native (X,Y,Z) = (X,Z,Y).
            var n = new NativeVector(
                uy * vz - uz * vy,
                ux * vy - uy * vx,
                uz * vx - ux * vz);
            n = n * (1d / n.Length());
            return n.Z < 0d ? n * -1d : n;
        }
    }

    private readonly record struct NativeVector(double X, double Y, double Z)
    {
        public double Dot(NativeVector other) => X * other.X + Y * other.Y + Z * other.Z;
        public double Length() => Math.Sqrt(Dot(this));
        public static NativeVector operator +(NativeVector a, NativeVector b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static NativeVector operator -(NativeVector a, NativeVector b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static NativeVector operator *(NativeVector a, double scalar) =>
            new(a.X * scalar, a.Y * scalar, a.Z * scalar);
    }
}
