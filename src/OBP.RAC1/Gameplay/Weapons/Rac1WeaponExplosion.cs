using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Ordinary, unupgraded weapon calls to explosion_effect (0x25e450).
/// This slice uses the unthinned F=0 recipe; hardware frame-load shedding,
/// the class639 dynamic light, sound and camera shake are separate.
/// </summary>
public sealed record Rac1WeaponExplosion(int Sparks, int Fireballs, int Puffs, int Debris,
    float ShellSize, float FlashSize, float DistantShellMinimum)
{
    public static Rac1WeaponExplosion ForWeapon(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.MineGlove => new(3, 3, 5, 1, 2, 1, 4),       // Air burst; ground uses type16.
        Rac1WeaponId.GloveOfDoom => new(5, 2, 4, 5, 2, 1, 9),     // 0x2c134c
        Rac1WeaponId.VisibombGun => new(10, 3, 16, 1, 4, 2, 0),   // 0x2b6a48
        Rac1WeaponId.Devastator or Rac1WeaponId.Ryno => new(10, 3, 9, 1, 4, 2, 9), // 0x2cdaec
        Rac1WeaponId.DroneDevice => new(0, 1, 1, 1, .4f, .2f, 4), // 0x2cec2c/2cf014
        _ => throw new ArgumentOutOfRangeException(nameof(weapon), "No recovered explosion recipe for this weapon.")
    };

    public IReadOnlyList<Rac1BombExplosion.Burst> Bursts(float cameraDistance)
    {
        var bursts = new List<Rac1BombExplosion.Burst>(4);
        if (ShellSize > 0)
        {
            if (cameraDistance > DistantShellMinimum)
            {
                bursts.Add(new(ShellSize, 16, new(150 / 128f, 150 / 128f, 150 / 128f, 32 / 128f)));
                bursts.Add(new(ShellSize, 22, new(127 / 128f, 127 / 128f, 80 / 128f, 32 / 128f)));
            }
            bursts.Add(new(ShellSize, 30, new(127 / 128f, 127 / 128f, 0, 48 / 128f)));
        }
        if (FlashSize > 0) bursts.Add(new(FlashSize, 27, new(255 / 128f, 255 / 128f, 255 / 128f, 32 / 128f)));
        return bursts;
    }

    public IReadOnlyList<Rac1BombExplosion.Fragment> Fragments(Vector3 origin, Vector3 camera, Rac1PickupRandom random)
    {
        var pieces = new List<Rac1BombExplosion.Fragment>(Debris);
        Vector3 toward = camera - origin;
        float distance = toward.Length();
        if (Debris == 0) return pieces;
        if (distance < 14)
        {
            toward.Z += distance * .5f;
            Vector3 jitter = new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1));
            Vector3 velocity = Rac1ParticleSession.SetLength(jitter, distance / (5 * 60)) +
                Rac1ParticleSession.SetLength(toward, distance * (2f / 60));
            if (velocity.Length() > 10f / 60) velocity = Rac1ParticleSession.SetLength(velocity, 10f / 60);
            Add(velocity, false);
        }
        for (int i = 0; i < Debris - 1; i++)
            Add(Spray(random, 1.308997f) + Vector3.UnitZ * (2f / 60), i % 3 == 0);
        return pieces;

        void Add(Vector3 velocity, bool trail)
        {
            int life = random.Inclusive(60, 90);
            Vector2 spin = new(random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60), random.Range(MathF.Tau / 60, MathF.Tau * 2 / 60));
            float scale = trail ? 1 : random.Range(.5f, .75f);
            pieces.Add(new(origin, velocity, Vector3.Zero, spin, life, scale, trail));
        }
    }

    internal static Vector3 Spray(Rac1PickupRandom random, float maximumPitch)
    {
        float pitch = random.Range(.2617994f, maximumPitch), yaw = random.Angle();
        float speed = random.Range(7, 10.5f) / 60;
        return new(MathF.Cos(yaw) * MathF.Cos(pitch) * speed,
            MathF.Sin(yaw) * MathF.Cos(pitch) * speed, MathF.Sin(pitch) * speed);
    }
}
