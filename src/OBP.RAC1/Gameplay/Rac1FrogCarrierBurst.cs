using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// FUN_L06_002e9f30: the class-827 owned crate's debris identities and
/// velocity/radius distributions. Host randomness does not reproduce the
/// retail global PRNG stream; fragments use the shared crate motion adapter.
/// No bolt payout is generated.
/// </summary>
public static class Rac1FrogCarrierBurst
{
    public const int PrimaryClass = 0x165;
    public const int SecondaryFirstClass = 0x166;
    public const int SecondaryLastClass = 0x167;
    public const int PrimaryCount = 4;
    public const int SecondaryCount = 8;
    public const int TotalCount = PrimaryCount + SecondaryCount;

    public static IReadOnlyList<Rac1CrateDebris> Create(Vector3 nativeParentPosition,
        Rac1PickupRandom random)
    {
        if (!float.IsFinite(nativeParentPosition.X) ||
            !float.IsFinite(nativeParentPosition.Y) ||
            !float.IsFinite(nativeParentPosition.Z))
            throw new ArgumentOutOfRangeException(nameof(nativeParentPosition));
        ArgumentNullException.ThrowIfNull(random);
        var output = new List<Rac1CrateDebris>(TotalCount);
        Vector3 origin = nativeParentPosition + new Vector3(0, 0, .5f);
        for (int i = 0; i < TotalCount; i++)
        {
            bool primary = i < PrimaryCount;
            int cls = primary ? PrimaryClass : SecondaryFirstClass + random.Below(2);
            float speed = random.Range(1, 4) / 60f;
            Vector3 offset = new(random.Range(-.625f, .625f),
                random.Range(-.625f, .625f), random.Range(0, 1.25f));
            Vector3 velocity = offset.LengthSquared() < 1e-8f
                ? Vector3.UnitZ * speed : Vector3.Normalize(offset) * speed;
            // The original effect call provides 0.3/0.6 for the four main
            // pieces and 0.5/0.7 for the eight smaller-class pieces. Retain
            // the ranges without claiming an exact vendor effect simulation.
            float scale = primary ? random.Range(.3f, .6f) : random.Range(.5f, .7f);
            output.Add(new Rac1CrateDebris(cls, origin + offset, velocity,
                new(random.Angle(), random.Angle(), random.Angle()), scale, 0, random));
        }
        return output;
    }
}
