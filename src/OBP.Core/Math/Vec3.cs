namespace OBP.Core.Math;

/// <summary>
/// Plain double-precision 3-vector for engine-independent geometry / bounds.
/// OBP world space is Y-up; native PS2 data is Z-up and is converted at the
/// world-assembly boundary (see the coordinate rule in the research notes).
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Native PS2 Z-up <c>(x, y, z)</c> to OBP Y-up <c>(x, z, y)</c>.</summary>
    public Vec3 NativeZUpToObpYUp() => new(X, Z, Y);
}

/// <summary>Axis-aligned bounds in OBP world space.</summary>
public readonly record struct ObpBounds(Vec3 Min, Vec3 Max)
{
    public Vec3 Center => (Min + Max) * 0.5;
    public double Diagonal => (Max - Min) is var d ? System.Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z) : 0;
}
