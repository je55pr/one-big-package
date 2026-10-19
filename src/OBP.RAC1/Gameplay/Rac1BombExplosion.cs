using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Unupgraded class79 effect recipes (2ae540..2aedbc), class122 fragment
/// recurrence (2af450/2af5b8) and class1192 burst recurrence (2acad8).
/// The host supplies its impact basis/random stream; native pooled sprites,
/// distance-dependent emission thinning and upgraded colours remain separate.
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
        public float Scale => Size * Age / Lifetime;
        public float Alpha => Color.W * MathF.Min(1, (Lifetime - Age) / (float)(Lifetime / 2));
        public bool Finished => Age >= Lifetime;
        public void Step() => Age++;
    }

    public sealed class Fragment(Vector3 position, Vector3 velocity, Vector3 angles, Vector2 spin, int lifetime, float scale)
    {
        public Vector3 Position { get; private set; } = position;
        public Vector3 Velocity { get; private set; } = velocity;
        public Vector3 Angles { get; private set; } = angles;
        public int Age { get; private set; }
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
        ? [new(4, 15, new(1, 1, 1, .25f)), new(4, 24, new(1, .25f, 0, .25f)), .. Bursts(false)]
        : [new(4, 20, new(1, .5f, 0, .375f)), new(3.5f, 27, new(.75f, .125f, 0, .5f)), new(3, 29, new(.25f, 0, 0, .25f))];

    public static IReadOnlyList<Fragment> Fragments(Vector3 origin, Rac1PickupRandom random)
    {
        var result = new List<Fragment>();
        for (int i = 0; i < FragmentCount; i++)
        {
            // OBP impact orientation: use the upper hemisphere until the full
            // native reflected impact-frame construction has been promoted.
            var direction = new Vector3(random.Range(-1, 1), random.Range(-1, 1), random.Range(.15f, 1));
            direction = Vector3.Normalize(direction);
            float speed = i < 10 ? random.Range(3.5f, 6.5f) : random.Range(6.5f, 10);
            int life = i < 10 ? random.Inclusive(60, 120) : random.Inclusive(60, 90);
            float scale = i < 10 ? random.Range(.5f, .75f) : 1;
            result.Add(new(origin, direction * (speed / 60),
                new(random.Angle(), random.Angle(), random.Angle()),
                new(random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60), random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60)), life, scale));
        }
        return result;
    }
}
