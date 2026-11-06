using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Particle kinds of the class-806 Nanotech orb cluster: 0x3e sparkles (update 0x273800), 0x3b hero-relative
/// glows (0x273088) and 0x3c drifting glints (0x273298). Native layouts are read as: 0x3e mode 1 never
/// changes, other modes fade their alpha by 127/life per frame until it reaches zero; 0x3b/0x3c are timed.
/// </summary>
public sealed partial class Rac1ParticleSession
{
    private const int SparkleKind = 0x3e, HeroGlowKind = 0x3b, GlintKind = 0x3c;
    public static readonly int SparkleType = SparkleKind, HeroGlowType = HeroGlowKind, GlintType = GlintKind;
    private const byte SparkleDepth = 0x44, GlowDepth = 0x21;
    public static readonly uint SparkleColor = 0x7f7f4040;

    /// <summary>The hero's native position, for particles that stay relative to Ratchet.</summary>
    public Vector3 Hero { get; set; }

    /// <summary>Persistent (mode 1) kind-0x3e sprites: the cloud orbs themselves.</summary>
    public int OrbSpriteCount => Live.Count(p => p.Type == SparkleType && p.Stage == 1);

    /// <summary>0x273708. Mode 1 is a persistent orb (caller moves and removes it); modes 0/2 fade over <paramref name="life"/> frames.</summary>
    public Particle? Sparkle(Vector3 position, int life, int mode, float rawSize) => Sparkle(position, life, mode, rawSize, SparkleColor);

    /// <summary>As above with an explicit colour word.</summary>
    public Particle? Sparkle(Vector3 position, int life, int mode, float rawSize, uint color)
    {
        var p = Allocate(SparkleType, Rac1NanotechOrbAssets.ParticleTextureTable, mode < 2 ? mode : 0, Blend.Additive, position, SparkleDepth);
        if (p is null) return null;
        p.Color = color; p.RawSize = rawSize; p.Stage = mode;
        // A persistent orb (mode 1) has no native life counter; the shared capture only draws particles with life left.
        p.Remaining = p.Total = life > 0 ? life : 1;
        // Retail truncates 127 / life, which is 0 (never fades) beyond 127 frames; callers use at most 40, and a particle must always expire, so fade at least 1 a frame.
        p.MiddleLife = life > 0 ? Math.Max(1, (int)(127f / life)) : 0;
        return p;
    }

    /// <summary>0x272f68: a glow that tracks the hero (offset kept) for <paramref name="life"/> frames. Size is in units (x 210000 raw).</summary>
    public Particle? HeroGlow(Vector3 position, uint color, byte frame, bool alphaBlend, int life, float size)
    {
        var p = Allocate(HeroGlowType, Rac1NanotechOrbAssets.ParticleTextureTable, 0, alphaBlend ? Blend.Alpha : Blend.Additive, position, GlowDepth);
        if (p is null) return null;
        p.Color = color; p.Angle = frame; p.RawSize = size * NativeSizeUnits;
        p.Remaining = p.Total = life; p.Relative = true; p.Offset = position - Hero;
        return p;
    }

    /// <summary>0x2730e0: a glint that drifts by <paramref name="velocity"/> per frame while keeping its offset from the hero.</summary>
    public Particle? Glint(Vector3 position, Vector3 velocity, uint color, int life, byte frame, float size)
    {
        var p = Allocate(GlintType, Rac1NanotechOrbAssets.GlintTextureTable, 0, Blend.Additive, position, GlowDepth);
        if (p is null) return null;
        p.Color = color; p.Angle = frame; p.RawSize = size * NativeSizeUnits; p.Velocity = velocity;
        p.Remaining = p.Total = life & 255; p.Relative = true; p.Offset = position - Hero;
        return p;
    }

    private void StepSparkle(Particle p)
    {
        if (p.Stage == 1) return;
        int alpha = (int)(p.Color >> 24) - p.MiddleLife;
        if (alpha <= 0) { Remove(p); return; }
        p.Color = (p.Color & 0xffffffu) | (uint)alpha << 24;
    }

    private void StepHeroGlow(Particle p)
    {
        if (p.Relative) p.Position = Hero + p.Offset;
        if (--p.Remaining <= 0) Remove(p);
    }

    private void StepGlint(Particle p)
    {
        if (p.Relative)
        {
            p.Position = Hero + p.Offset + p.Velocity;
            p.Offset = p.Position - Hero;
        }
        else p.Position += p.Velocity;
        if (--p.Remaining <= 0) { Remove(p); return; }
        // rgba_lerp(rgb | alpha 0, colour, (life-1)/life): the alpha shrinks by one frame's share.
        uint alpha = (uint)(int)((p.Color >> 24) * ((p.Remaining) / (float)(p.Remaining + 1)));
        p.Color = (p.Color & 0xffffffu) | alpha << 24;
    }
}
