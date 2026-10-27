using System.Numerics;

namespace OBP.RAC2.Gameplay;

/// <summary>Scalar EE/VU add/subtract/multiply chop; EE DIV.S rounds nearest. Scoped to the measured GC path.</summary>
internal static class Rac2ParticleMath
{
    public static float Add(float a, float b) => Chop((double)a + b);
    public static float Subtract(float a, float b) => Chop((double)a - b);
    public static float Multiply(float a, float b) => Chop((double)a * b);
    public static float Divide(float a, float b) => (float)((double)a / b);
    public static float VuDivide(float a, float b) => Chop((double)a / b);
    public static float FromInt(int value) => Chop(value);
    public static Vector3 Add(Vector3 a, Vector3 b) => new(Add(a.X, b.X), Add(a.Y, b.Y), Add(a.Z, b.Z));

    private static float Chop(double value)
    {
        float rounded = (float)value;
        if (float.IsInfinity(rounded)) return MathF.CopySign(float.MaxValue, rounded);
        if (rounded > 0 && rounded > value) rounded = MathF.BitDecrement(rounded);
        else if (rounded < 0 && rounded < value) rounded = MathF.BitIncrement(rounded);
        return MathF.Abs(rounded) < 1.17549435E-38f ? MathF.CopySign(0, rounded) : rounded;
    }
}
