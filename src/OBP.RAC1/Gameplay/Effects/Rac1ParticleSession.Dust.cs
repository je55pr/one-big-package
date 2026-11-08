using System.Numerics;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    // 26f080/26f280, also used for the Hydro-Pack's underwater exhaust.
    public Particle? Dust(Vector3 position, Vector3 velocity, float rawSize, float ceiling)
    {
        var p = Allocate(0x22, 34, 0, Blend.Alpha, position, 0x44);
        if (p is null) return null;
        p.Color = 0x88a0a0a0; p.RawSize = 5250;
        p.BaseSize = random.Range(.75f, 1.25f) * rawSize;
        p.Angle = unchecked((byte)(random.Inclusive(-10, 10) - 128));
        p.Remaining = p.Total = random.Inclusive(70, 140);
        p.Reach = ceiling;
        p.DustPhase = random.Angle();
        p.DustPhaseStep = random.Range(Dt * 1.7453293f, Dt * 6.981317f);
        p.DustSway = random.Range(Dt * .5f, Dt * 1.4f);
        p.Velocity = velocity;
        return p;
    }

    private void StepDust(Particle p, float heading)
    {
        if (--p.Remaining <= 0) { Remove(p); return; }
        var velocity = p.Velocity;
        if (p.Reach < p.Position.Z) { velocity.Z = 0; p.Remaining = Math.Min(p.Remaining, 12); }
        if (p.Reach < p.Position.Z + Dt * 1.3f * 40 && random.Below(20) == 0)
            p.Remaining = Math.Min(p.Remaining, 12);
        if (p.Remaining < 14) p.Color = (p.Color & 0xffffff) | (uint)((p.Color >> 24) * p.Remaining / 14) << 24;
        p.RawSize = Math.Min(p.BaseSize, p.RawSize + BitConverter.Int32BitsToSingle(0x44520001));
        velocity.X *= 1 + BitConverter.Int32BitsToSingle(unchecked((int)0xbcf5c280));
        velocity.Y *= 1 + BitConverter.Int32BitsToSingle(unchecked((int)0xbcf5c280));
        p.Position += velocity;
        velocity.Z += Math.Clamp(Dt * 1.3f - velocity.Z, -5f / 3600, 5f / 3600);
        p.Velocity = velocity;
        p.DustPhase += p.DustPhaseStep;
        float sway = MathF.Sin(p.DustPhase) * p.DustSway;
        p.Position += new Vector3(MathF.Cos(heading + MathF.PI / 2), MathF.Sin(heading + MathF.PI / 2), 0) * sway;
    }
}
