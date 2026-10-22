using System.Numerics;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    // 0x26bed0/26c088, particle type0x10; kind3 reuses kind0's texture.
    public Particle? Steam(Vector3 position, Vector3 velocity, uint startColor, uint endColor,
        int lifetime, int kind, float rawSize)
    {
        if ((uint)kind > 3) throw new ArgumentOutOfRangeException(nameof(kind));
        if (lifetime <= 0) return null;
        var p = Allocate(16, 16, kind == 3 ? 0 : kind, Blend.Alpha, position);
        if (p is null) return null;
        p.Velocity = velocity; p.StartColor = p.Color = startColor; p.EndColor = endColor;
        p.Remaining = p.Total = lifetime; p.Stage = kind;
        p.RawSize = p.BaseSize = rawSize; p.Reach = position.Z - .5f;
        return p;
    }

    private void StepSteam(Particle p, Vector3 camera)
    {
        if (--p.Remaining <= 0) { Remove(p); return; }
        p.Color = LerpColor(p.EndColor, p.StartColor, p.Remaining / (float)p.Total);
        int alpha = (int)(p.Color >> 24);
        float distance = Vector3.Distance(p.Position, camera);
        if (distance < 4) { Remove(p); return; }
        if (p.Remaining < 6) alpha = alpha * p.Remaining / 6;
        if (distance < 8) alpha = (int)(alpha * (distance - 4) * .25f);
        p.Color = (p.Color & 0xffffff) | (uint)alpha << 24;
        p.Position += p.Velocity;
        float gravity = p.Stage switch { 0 => 4.8f, 1 => 24.5f, 2 => 9.8f, _ => -.24f };
        p.Velocity -= Vector3.UnitZ * (gravity * Dt * Dt);
        if (p.Position.Z >= p.Reach) { p.Angle++; return; }
        if (p.Stage == 1)
        {
            uint a = LerpColor(0x7f000000, 0x7f182030, random.Range(.25f, 1));
            uint b = LerpColor(0, 0x005f5f5f, random.Range(.5f, 1));
            Steam(p.Position, new(p.Velocity.X, p.Velocity.Y, 0), a, b, random.Inclusive(90, 150), 3,
                random.Range(200000, 300000));
        }
        Remove(p);
    }

    // mine_update (0x2aa670), ordinary ground branch; the authored
    // camera-distance count is bounded by the shared allocation capacity.
    public void MineGroundExplosion(Vector3 origin, Vector3 camera)
    {
        int count = (int)(new Vector2(origin.X - camera.X, origin.Y - camera.Y).Length() * 15);
        for (int i = 0; i < count && Count < _slots.Length; i++)
        {
            int kind = random.Below(4);
            float upper = kind switch { 0 => 14, 1 => 22, 2 => 12, _ => 0 };
            Vector3 velocity = new(random.Range(-.5f, .5f) * Dt, random.Range(-.5f, .5f) * Dt, random.Range(4 * Dt, upper * Dt));
            float radius = random.Range(0, kind == 1 ? .5f : .25f), yaw = random.Angle();
            Vector3 position = origin + new Vector3(MathF.Cos(yaw) * radius, MathF.Sin(yaw) * radius, -radius * .70710677f);
            velocity.Z -= radius * Dt * 8;
            switch (kind)
            {
                case 0:
                    Steam(position, velocity, 0x1f101820, 0x00101010, random.Inclusive(180, 240), 0, random.Range(200000, 300000));
                    break;
                case 1:
                    Steam(position, velocity, 0x3f081020, 0x0f081020, 180, 1, random.Range(50000, 100000));
                    break;
                case 2:
                    uint color = random.Below(100) > 39 ? 0x2f486078u : 0x5ff8f8f8u;
                    Steam(position, velocity, color, 0x0f000020, random.Inclusive(30, 45), 2, 150000);
                    break;
                case 3:
                    uint a = LerpColor(0x7f000000, 0x7f182030, random.Range(.25f, 1));
                    uint b = LerpColor(0, 0x005f5f5f, random.Range(.5f, 1));
                    float speed = random.Range(0, 1) * Dt, pitch = random.Angle();
                    Vector3 drift = new(MathF.Cos(yaw) * speed * MathF.Cos(pitch),
                        MathF.Sin(yaw) * speed * MathF.Cos(pitch), MathF.Sin(pitch) * speed);
                    Steam(position, drift, a, b, random.Inclusive(240, 300), 3, random.Range(200000, 300000));
                    break;
            }
        }
    }
}
