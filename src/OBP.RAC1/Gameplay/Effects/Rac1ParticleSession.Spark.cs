using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Kind 0x45 (spawn 0x274948, update 0x274a70): a grey additive spark that drifts by its velocity, turns at
/// random and fades linearly over its life. Used by the class-1471 beam ends and the class-1440 lamp flash.
/// The owner-attached modes (1/2/3/4) are not needed by those callers and are not modelled.
/// </summary>
public sealed partial class Rac1ParticleSession
{
    private const int SparkKind = 0x45;
    public static readonly int SparkType = SparkKind;
    /// <summary>Part lookup entry 69 (the word at overlay 0x1b2194); the first byte is the texture.</summary>
    public static readonly int SparkTextureTable = 69;
    private const byte SparkDepth = 0x74;
    private const uint SparkRgb = 0x7f7f7fu;
    private const byte SparkAlpha = 0x7f;

    /// <summary>
    /// <paramref name="rawSize"/> null draws the native 8000..50000; <paramref name="life"/> null draws 10..29 frames.
    /// Alpha starts at <paramref name="alpha"/> (0x7f) and falls to zero over the life.
    /// </summary>
    public Particle? Spark(Vector3 position, Vector3 velocity) => SparkCore(position, velocity, null, null);

    public Particle? Spark(Vector3 position, Vector3 velocity, float rawSize) => SparkCore(position, velocity, rawSize, null);

    public Particle? Spark(Vector3 position, Vector3 velocity, float rawSize, int life) => SparkCore(position, velocity, rawSize, life);

    private Particle? SparkCore(Vector3 position, Vector3 velocity, float? rawSize, int? life)
    {
        var p = Allocate(SparkType, SparkTextureTable, 0, Blend.Additive, position, SparkDepth);
        if (p is null) return null;
        p.RawSize = rawSize ?? random.Range(8000, 50000);
        p.Remaining = p.Total = Math.Max(1, life ?? random.Inclusive(10, 29));
        p.Velocity = velocity; p.Stage = SparkAlpha;
        p.Color = (uint)SparkAlpha << 24 | SparkRgb;
        return p;
    }

    private void StepDriftSpark(Particle p)
    {
        p.Position += p.Velocity;
        int turn = (int)(random.Signed(0, 64));
        int angle = p.Angle + turn;
        if (angle >= 0x100) angle -= 0xff;
        p.Angle = unchecked((byte)angle);
        if (--p.Remaining <= 0) { Remove(p); return; }
        // alpha = frames left * (1 / life) * start alpha, over the grey colour word.
        p.Color = (uint)(int)(p.Remaining * (1f / p.Total) * p.Stage) << 24 | SparkRgb;
    }
}
