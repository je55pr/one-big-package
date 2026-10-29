using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Explicit OBP reconstruction choices where native query/motion/callback
/// recovery is incomplete. These are not retail constants. Kept separate from
/// Rac1WeaponProfiles and outside the Godot application/presentation layer.
/// </summary>
public static class Rac1WeaponReconstructionPolicy
{
    public const float BlasterAimRange = 40;
    public const int FlameVictimRepeatTicks = 12;
    public const int FlameLifetimeTicks = 24;
    public const int FlameEmissionTicks = 2;
    public const float MineSeekRange = 5;
    public const float MineDetonationDistance = 1.1f;
    public const float MineSeekStep = 3f / 60;
    public const int PlantedLifetimeTicks = 1800;
    public const float DoomSearchRange = 15;
    public const float DoomContactDistance = .9f;
    public const float DoomRunSpeed = 4;
    public const int DoomLifetimeTicks = 1800;
    public const float SuckCaptureRange = 6;
    public const float SuckCaptureConeDot = .82f;
    public const int SuckCaptureTicks = 18;
    public const float SuckProjectileStep = 24f / 60;
    public const int SuckProjectileLifetimeTicks = 90;
    public const double SuckDamage = 1;
    public const uint SuckDamageFlags = 0x00010000;
}

/// <summary>Provisional engine-independent bot pursuit; the host supplies candidates/support/contact.</summary>
public sealed class Rac1DoomAgentSession
{
    public int Age { get; private set; }
    public Vector3 Wander { get; set; }
    public Vector3 Direction(Vector3 origin, Vector3? target)
    {
        var delta = (target ?? (origin + Wander)) - origin;
        delta.Z = 0;
        return delta.LengthSquared() > .0001f ? Vector3.Normalize(delta) : Vector3.Zero;
    }
    public bool Step(Vector3 origin, Vector3? target) => ++Age > Rac1WeaponReconstructionPolicy.DoomLifetimeTicks ||
        target.HasValue && Vector3.Distance(origin, target.Value) < Rac1WeaponReconstructionPolicy.DoomContactDistance;
    public int SourceSequence(bool moving) => moving ? 7 : 6;
}
