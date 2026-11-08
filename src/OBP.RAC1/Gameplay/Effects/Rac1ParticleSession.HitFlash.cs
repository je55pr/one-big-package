using System.Numerics;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    /// <summary>26cbb0 / 26ccb0: kind-15 flash and its optional alternating-frame children.</summary>
    public Particle? HitFlash(Vector3 position, Vector3 velocity, uint start, uint end, int life, float size, bool children)
    {
        if (life <= 0) return null;
        var p = Allocate(0x15, 21, 0, Blend.Additive, position, 0xa4);
        if (p is null) return null;
        p.Color = p.StartColor = start; p.EndColor = end;
        p.Remaining = p.Total = life; p.RawSize = p.BaseSize = size;
        p.Velocity = velocity; p.Stage = children ? 1 : 0;
        return p;
    }

    private void StepHitFlash(Particle p)
    {
        if (--p.Remaining <= 0) { Remove(p); return; }
        float amount = p.Remaining / (float)p.Total;
        p.RawSize = amount * p.BaseSize;
        p.Color = LerpColor(p.EndColor, p.StartColor, amount);
        p.Position += p.Velocity;
        p.Velocity -= new Vector3(0, 0, .001f);
        if (p.Stage != 0 && (_tick & 1) != 0)
        {
            var direction = SetLength(new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1)),
                p.Velocity.Length() * random.Range(.21f, .25f));
            direction = (direction + p.Velocity) * random.Range(.75f, .95f);
            HitFlash(p.Position, direction, p.Color, p.EndColor, random.Inclusive(15, 20), p.RawSize, false);
        }
        else p.Velocity *= .96f;
    }
}
