using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The Veldin shooting robot's per-update flash (0x2e1aa8): at its second chain point, lowered by .333, one
/// slow drifting spark and three 2-frame flares (80000..120000 raw, one time in eight 180000 for the third).
/// Native positions the flares through an owner-attached mode; they live two frames, so they sit at the point.
/// </summary>
public static class Rac1RobotLampFlash
{
    public static readonly float Lowering = 0.333f;

    public static void Emit(Rac1ParticleSession particles, Rac1PickupRandom random, Vector3 lampSocket)
    {
        var at = lampSocket - Vector3.UnitZ * Lowering;
        particles.Spark(at, Rac1BeamSlot.RandomDirection(random, .005f, .03f), random.Range(6000, 32000));
        for (int i = 0; i < 3; i++)
        {
            float size = i == 2 && random.Below(8) == 0 ? 180000 : random.Range(80000, 120000);
            particles.Spark(at, Vector3.Zero, size, 2);
        }
    }
}
