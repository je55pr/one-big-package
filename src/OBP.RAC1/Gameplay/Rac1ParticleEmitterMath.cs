using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Type-6's scalar instruction order: add/subtract/multiply chop, FPU division rounds nearest.
/// This is deliberately local to the recovered emitter; other particle families are unchanged.
/// Host sine/cosine and vector normalisation are bounded projections of native VU helpers.
/// </summary>
internal static class Rac1ParticleEmitterMath
{
    public static float Add(float a, float b) => Truncate((double)a + b);
    public static float Sub(float a, float b) => Truncate((double)a - b);
    public static float Mul(float a, float b) => Truncate((double)a * b);
    public static float Div(float a, float b) => (float)((double)a / b);
    public static Vector3 Add(Vector3 a, Vector3 b) => new(Add(a.X, b.X), Add(a.Y, b.Y), Add(a.Z, b.Z));

    private static float Truncate(double value)
    {
        float rounded = (float)value;
        if (float.IsInfinity(rounded)) return MathF.CopySign(float.MaxValue, rounded);
        if (rounded > 0 && rounded > value) rounded = MathF.BitDecrement(rounded);
        else if (rounded < 0 && rounded < value) rounded = MathF.BitIncrement(rounded);
        return MathF.Abs(rounded) < 1.17549435E-38f ? MathF.CopySign(0, rounded) : rounded;
    }

    public static uint Pack(Vector3 rgb, float alpha) =>
        (uint)(int)Mul(rgb.X, 255) & 255u | ((uint)(int)Mul(rgb.Y, 255) & 255u) << 8 |
        ((uint)(int)Mul(rgb.Z, 255) & 255u) << 16 | unchecked((uint)(int)Mul(alpha, 255) << 24);

    public static float AddAngle(float a, float b)
    {
        float sum = Add(a, b);
        return sum >= MathF.PI ? Sub(Sub(sum, MathF.PI), MathF.PI) :
            sum < -MathF.PI ? Add(Add(sum, MathF.PI), MathF.PI) : sum;
    }

    public static Vector3 Rotate(Vector3 vector, Vector3 angles)
    {
        float cx = MathF.Cos(angles.X), sx = MathF.Sin(angles.X);
        float cy = MathF.Cos(angles.Y), sy = MathF.Sin(angles.Y);
        float cz = MathF.Cos(angles.Z), sz = MathF.Sin(angles.Z);
        // Native Z-up Euler rows, the same composition as the Moby transform.
        Vector3 x = new(Mul(cz, cy), Mul(sz, cy), -sy);
        Vector3 y = new(Sub(Mul(Mul(cz, sy), sx), Mul(sz, cx)),
            Add(Mul(Mul(sz, sy), sx), Mul(cz, cx)), Mul(cy, sx));
        Vector3 z = new(Add(Mul(Mul(cz, sy), cx), Mul(sz, sx)),
            Sub(Mul(Mul(sz, sy), cx), Mul(cz, sx)), Mul(cy, cx));
        return new(Add(Add(Mul(x.X, vector.X), Mul(y.X, vector.Y)), Mul(z.X, vector.Z)),
            Add(Add(Mul(x.Y, vector.X), Mul(y.Y, vector.Y)), Mul(z.Y, vector.Z)),
            Add(Add(Mul(x.Z, vector.X), Mul(y.Z, vector.Y)), Mul(z.Z, vector.Z)));
    }

    public static Vector3 Limit(Vector3 velocity, float limit)
    {
        float square = Add(Add(Mul(velocity.X, velocity.X), Mul(velocity.Y, velocity.Y)), Mul(velocity.Z, velocity.Z));
        float length = Truncate(Math.Sqrt(square));
        if (length == 0 || Sub(limit, length) > 0) return velocity;
        float scale = Truncate((double)limit / length); // VU division chops, unlike FPU DIV.S.
        return new(Mul(velocity.X, scale), Mul(velocity.Y, scale), Mul(velocity.Z, scale));
    }
}
