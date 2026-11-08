using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The two Novalis pool particle kinds the class-751 pool controller spawns in the cave tank zone: kind 0x38, a small
/// bubble (spawn 0x287b00, update 0x272838) and kind 0x39, a large foam sprite (spawn 0x287c80, update 0x2728e8).
/// Both are additive, share part texture lookup entries 0x38 / 0x39 and grow by a fixed size step per native frame.
/// Positions are native level coordinates (x, y, z with z up), like the other particle kinds in this session.
/// </summary>
public sealed partial class Rac1ParticleSession
{
    private const int PoolBubbleKind = 0x38, PoolFoamKind = 0x39;
    public static readonly int PoolBubbleType = PoolBubbleKind, PoolFoamType = PoolFoamKind;
    private const byte PoolBubbleDepth = 0x24, PoolFoamDepth = 0x2c;   // alpha byte (+9) set by each constructor
    private const float PoolBubbleGrowth = 6300f, PoolFoamGrowth = 5880f;
    private const int PoolFoamLife = 0x80;

    /// <summary>
    /// A bubble at <paramref name="position"/> with the given raw size (native 4..10). It sits still (its velocity is zero),
    /// grows 6300 per frame, and its grey (0x60..0x7e, even) loses 4 per frame until it is gone.
    /// </summary>
    public Particle? PoolBubble(Vector3 position, float rawSize)
    {
        var p = Allocate(PoolBubbleKind, PoolBubbleKind, 0, Blend.Additive, position, PoolBubbleDepth);
        if (p is null) return null;
        int grey = random.Below(0x10) * 2 + 0x60;
        p.RawSize = rawSize;
        p.Velocity = Vector3.Zero;
        p.Color = 0x40000000u | (uint)grey << 16 | (uint)grey << 8 | (uint)grey;
        p.Remaining = p.Total = grey / 4;   // presentation only: the native update ends by colour, but the sprite capture skips particles with no life left
        return p;
    }

    /// <summary>
    /// A foam sprite: it drifts by <paramref name="velocity"/> per frame, grows 5880 per frame, spins with a decaying
    /// rate (<paramref name="speed"/> * 0.98 each frame) and lives 128 frames, fading in over the first 64 and out over the last.
    /// <paramref name="scale"/> is multiplied by the native size unit (210000).
    /// </summary>
    public Particle? PoolFoam(Vector3 position, Vector3 velocity, float scale, float speed)
    {
        var p = Allocate(PoolFoamKind, PoolFoamKind, 0, Blend.Additive, position, PoolFoamDepth);
        if (p is null) return null;
        p.Color = (uint)random.Below(0x10) << 25 | 0x808080u;
        p.Remaining = p.Total = PoolFoamLife;
        p.RawSize = scale * NativeSizeUnits;
        p.Velocity = velocity;
        float spin = random.Range(0f, 256f);
        p.EmitterAngle = spin;      // the native angle accumulator (data +0x10)
        p.Angle = unchecked((byte)(int)spin);
        p.Speed = speed;            // the native turn rate (data +0x14)
        return p;
    }

    private void StepPoolBubble(Particle p)
    {
        p.Velocity *= 0.975f;
        p.Position += p.Velocity;
        p.RawSize += PoolBubbleGrowth;
        int grey = (int)(p.Color & 0xff) - 4;
        if (grey <= 0) { Remove(p); return; }
        p.Color = 0x60000000u | (uint)grey << 16 | (uint)grey << 8 | (uint)grey;
    }

    private void StepPoolFoam(Particle p)
    {
        p.Position += p.Velocity;
        p.RawSize += PoolFoamGrowth;
        p.Speed *= 0.98f;
        p.EmitterAngle += p.Speed;
        p.Angle = unchecked((byte)(int)p.EmitterAngle);
        if (--p.Remaining <= 0) { Remove(p); return; }
        p.Color = (p.Remaining > 0x40 ? (uint)(0x80 - p.Remaining) : (uint)p.Remaining) << 24 | 0x808080u;
    }
}
