using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Unupgraded class79 effect recipes (2ae540..2aedbc), class122 fragment
/// recurrence (2af450/2af5b8) and class1192 burst recurrence (2acad8).
/// The host supplies the resolved impact normal/reflected push and camera.
/// Particle allocation and upgraded colours remain separate.
/// </summary>
public static class Rac1BombExplosion
{
    public const int FragmentClass = 122;
    public const int BurstClass = 1192;
    public const int FragmentCount = 15;
    public const float FragmentGravity = 14.6f * (1f / 3600);
    public const float BurstSpin = MathF.PI * Rac1BombProjectileSession.NativeTickScale;

    public sealed record Burst(float Size, int Lifetime, Vector4 Color)
    {
        public int Age { get; private set; }
        // shard_update computes scale before decrementing its lifetime.
        public float Scale => Size * Math.Max(0, Age - 1) / Lifetime;
        public float Alpha => Color.W * MathF.Min(1, (Lifetime - Age) / (float)(Lifetime / 2));
        public bool Finished => Age >= Lifetime;
        public void Step() => Age++;
    }

    public sealed class Fragment(Vector3 position, Vector3 velocity, Vector3 angles, Vector2 spin, int lifetime, float scale, bool trail = false)
    {
        public Vector3 Position { get; private set; } = position;
        public Vector3 Velocity { get; private set; } = velocity;
        public Vector3 Angles { get; private set; } = angles;
        public int Age { get; private set; }
        public bool Trail { get; } = trail;
        public float Scale => scale * MathF.Min(1, (lifetime - Age) / (float)(lifetime / 4));
        public bool Finished => Age >= lifetime || Position.X < 0 || Position.Y < 0 || Position.Z < 0;
        public void Step()
        {
            Position += Velocity;
            Velocity = new(Velocity.X, Velocity.Y, Velocity.Z - FragmentGravity);
            Angles += new Vector3(spin, 0);
            Age++;
        }
    }

    public static IReadOnlyList<Burst> Bursts(bool distant) => distant
        ? [new(4, 15, new(127 / 128f, 127 / 128f, 127 / 128f, 32 / 128f)),
            new(4, 24, new(127 / 128f, 32 / 128f, 0, 32 / 128f)), .. Bursts(false)]
        : [new(4, 20, new(127 / 128f, 63 / 128f, 0, 48 / 128f)),
            new(3.5f, 27, new(96 / 128f, 16 / 128f, 0, 64 / 128f)),
            new(3, 29, new(32 / 128f, 0, 0, 32 / 128f))];

    public static IReadOnlyList<Fragment> Fragments(Vector3 origin, Rac1PickupRandom random,
        Vector3? impactNormal = null, Vector3? reflectedPush = null, Vector3? camera = null)
    {
        Vector3 normal = Rac1ParticleSession.SetLength(impactNormal ?? Vector3.UnitZ, 1);
        Vector3 push = reflectedPush ?? Vector3.UnitZ * (2f / 60);
        var result = new List<Fragment>();
        for (int i = 0; i < FragmentCount; i++)
        {
            var direction = new Vector3(random.Range(-1, 1), random.Range(-1, 1), 0);
            direction -= normal * Vector3.Dot(direction, normal);
            direction += normal * random.Range(0, 1);
            float speed = i < 10 ? random.Range(3.5f, 6.5f) : random.Range(6.5f, 10);
            Vector3 velocity = Rac1ParticleSession.SetLength(direction, speed / 60) + push;
            if (i == 14 && camera is { } eye)
            {
                Vector3 toward = eye - origin;
                float distance = toward.Length();
                toward.Z += distance * .5f;
                Vector3 jitter = new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1));
                velocity = Rac1ParticleSession.SetLength(jitter, distance / (5 * 60)) +
                    Rac1ParticleSession.SetLength(toward, distance * (2f / 60));
                if (velocity.Length() > 10f / 60) velocity = Rac1ParticleSession.SetLength(velocity, 10f / 60);
            }
            int life = i < 10 ? random.Inclusive(60, 120) : random.Inclusive(60, 90);
            float scale = i < 10 ? random.Range(.5f, .75f) : 1;
            result.Add(new(origin, velocity, Vector3.Zero,
                new(random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60), random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60)), life, scale,
                trail: i >= 10));
        }
        return result;
    }
}
