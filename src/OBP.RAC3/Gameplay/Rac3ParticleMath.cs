using System.Numerics;

namespace OBP.RAC3.Gameplay;

/// <summary>Measured TABLE1 EE/VU arithmetic for type 97 and its two Cannon recipes.</summary>
internal static class Rac3ParticleMath
{
    public static float Add(float a, float b) => Chop((double)a + b);
    public static Vector3 Add(Vector3 a, Vector3 b) => new(Add(a.X, b.X), Add(a.Y, b.Y), Add(a.Z, b.Z));
    public static float Subtract(float a, float b) => Chop((double)a - b);
    public static float Multiply(float a, float b) => Chop((double)a * b);
    // TABLE1 0x46dda4 DIV.S (46001083); nearest/ties-even in the pinned EE oracle's ordinary finite domain.
    public static float Divide(float a, float b) => (float)((double)a / b);
    public static float VuDivide(float a, float b) => Chop((double)a / b);
    public static float FromInt(int value) => Chop(value);

    // 0x40be68: VADDA / VMSUBA / VMADD, retaining A.w, not a host Vector4.Lerp.
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => new(
        Add(Subtract(a.X, Multiply(a.X, t)), Multiply(b.X, t)),
        Add(Subtract(a.Y, Multiply(a.Y, t)), Multiply(b.Y, t)),
        Add(Subtract(a.Z, Multiply(a.Z, t)), Multiply(b.Z, t)), a.W);

    private static float Chop(double value)
    {
        float rounded = (float)value;
        if (float.IsInfinity(rounded)) return MathF.CopySign(float.MaxValue, rounded);
        if (rounded > 0 && rounded > value) rounded = MathF.BitDecrement(rounded);
        else if (rounded < 0 && rounded < value) rounded = MathF.BitIncrement(rounded);
        return MathF.Abs(rounded) < 1.17549435E-38f ? MathF.CopySign(0, rounded) : rounded;
    }
}
