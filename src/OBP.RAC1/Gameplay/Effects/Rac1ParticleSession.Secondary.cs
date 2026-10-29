using System.Numerics;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    // 0x2b8438 emits two type2 particles when a tracked flame has ten frames left.
    public void FlameSecondary(Particle parent)
    {
        if (parent.Type != 12 || parent.Remaining != 10 || parent.AuxiliaryLife != 0) return;
        parent.AuxiliaryLife = 1;
        for (int i = 0; i < 2; i++)
        {
            Vector3 end = Sphere(0, Dt); end.Z = 4 * Dt;
            uint a = LerpColor(0x30002040, 0x30004040, random.Range(0, 1));
            uint b = LerpColor(0x10002040, 0x10004040, random.Range(0, 1));
            int middle = (int)random.Range(10, 20), tail = (int)random.Range(10, 20);
            var p = Allocate(2, 12, 0, Blend.Additive, parent.Position + Sphere(0, parent.Size));
            if (p is null) continue;
            p.Velocity = QuantizeDirection(parent.Velocity); p.EndVelocity = QuantizeDirection(end);
            p.StartColor = p.Color = a; p.EndColor = b;
            p.BaseSize = 42 * 1000; p.EndSize = 21 * 1000;
            p.Remaining = p.Total = 10; p.MiddleLife = middle; p.FinalLife = tail;
        }
    }

    // 0x261480/2615a8 preserve the source's packed direction quantization.
    public static Vector3 QuantizeDirection(Vector3 direction)
    {
        float top = Math.Max(Math.Max(Math.Abs(direction.X), Math.Abs(direction.Y)), Math.Abs(direction.Z));
        int n = Math.Clamp((int)(top * 10000 / 63), 1, 255);
        float unit = n * .0001f, k = 1 / unit;
        float Q(float x) => (((int)(x * k + 127) & 255) - 127) * unit;
        return new(Q(direction.X), Q(direction.Y), Q(direction.Z));
    }

    private void StepSecondary(Particle p)
    {
        if ((_tick & 1) != 0) p.Angle++;
        float t = p.Remaining / (float)p.Total;
        if (p.Stage == 0)
        {
            p.Position += p.Velocity; p.RawSize = p.BaseSize;
            p.Color = LerpColor(p.StartColor & 0xffffff, p.StartColor, 1 - t);
        }
        else if (p.Stage == 1)
        {
            p.Position += Vector3.Lerp(p.EndVelocity, p.Velocity, t);
            p.RawSize = (p.BaseSize - p.EndSize) * t + p.EndSize;
            p.Color = LerpColor(p.EndColor, p.StartColor, t);
        }
        else
        {
            p.Position += p.EndVelocity; p.RawSize = p.EndSize;
            p.Color = LerpColor(p.EndColor & 0xffffff, p.EndColor, t);
        }
        if (--p.Remaining > 0) return;
        if (++p.Stage == 3) { Remove(p); return; }
        p.Remaining = p.Total = p.Stage == 1 ? p.MiddleLife : p.FinalLife;
    }

    // 0x26a4c8/26a5b8: a growing additive puff with ten authored texture frames.
    public Particle? Glow(Vector3 position, Vector3 velocity, uint startColor, uint endColor, int lifetime, float rawSize)
    {
        if (lifetime <= 0) return null;
        var p = Allocate(8, 8, 0, Blend.Additive, position);
        if (p is null) return null;
        p.Velocity = velocity; p.StartColor = p.Color = startColor; p.EndColor = endColor;
        p.Remaining = p.Total = lifetime; p.BaseSize = rawSize / lifetime; p.Angle = 0;
        return p;
    }

    private void StepGlow(Particle p)
    {
        p.Position += p.Velocity;
        if (--p.Remaining <= 0) { Remove(p); return; }
        p.RawSize += p.BaseSize;
        p.Color = LerpColor(p.EndColor, p.StartColor, p.Remaining / (float)p.Total);
        if (p.Remaining < 6) p.Color = (p.Color & 0xffffff) | (uint)((p.Color >> 24) * p.Remaining / 6) << 24;
        p.Texture = assets.ParticleChoices[8][(p.Total - p.Remaining) * 10 / p.Total];
    }

    // 0x26bb70/26bca8: ballistic sparks, optionally shedding smaller children.
    public Particle? Spark(Vector3 position, Vector3 velocity, uint startColor, uint endColor,
        int lifetime, bool trail, float rawSize)
    {
        if (lifetime <= 0) return null;
        var p = Allocate(15, 15, 0, Blend.Additive, position);
        if (p is null) return null;
        p.Velocity = velocity; p.StartColor = p.Color = startColor; p.EndColor = endColor;
        p.Remaining = p.Total = lifetime; p.Flags = trail ? (byte)1 : (byte)0;
        p.RawSize = p.BaseSize = rawSize;
        return p;
    }

    private void StepSpark(Particle p)
    {
        if (--p.Remaining <= 0) { Remove(p); return; }
        p.RawSize = p.Remaining * p.BaseSize / p.Total;
        p.Color = LerpColor(p.EndColor, p.StartColor, p.Remaining / (float)p.Total);
        p.Position += p.Velocity; p.Velocity -= new Vector3(0, 0, 14.6f * Dt * Dt);
        if ((p.Flags & 1) != 0 && (_tick & 1) != 0)
        {
            Vector3 jitter = new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1));
            Vector3 velocity = (SetLength(jitter, p.Velocity.Length() * random.Range(.15f, .25f)) + p.Velocity) * random.Range(.75f, .95f);
            Spark(p.Position, velocity, p.Color, p.EndColor, random.Inclusive(15, 30), false, p.RawSize);
        }
        else p.Velocity *= .96f;
    }
}
