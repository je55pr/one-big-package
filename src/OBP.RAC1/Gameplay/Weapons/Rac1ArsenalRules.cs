using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Weapon-specific rules recovered using rac1-native-game as an address map,
/// checked against NTSC-U LEVEL0. Query geometry and incomplete effects remain
/// explicitly bounded by Rac1ArsenalHostPolicy. See research/RAC1_ARSENAL.md.
/// </summary>
public static class Rac1ArsenalRules
{
    public const int DroneCapacity = 6; // use_agents, 0x2cdcc0.
    public const int RynoBarrels = 9; // 0x2ccb90 / 0x2ccbdc.
    public const int RynoMissilesPerUse = 7; // reviewed firing-state exit, 0x2ccd..
    public const int RynoMissileInterval = 9;
    public const int VisibombLifetime = 3000; // 0x2b59c0.
    public const int RocketLifetime = 300; // 0x2afddc.
    public const int RynoRocketLifetime = 250; // 0x2ccff8.
    public const float RynoInitialSpeed = 8; // 0x2cd09c.
    public const float RynoMaximumSpeed = 20; // 0x2cd26c.
    public const float RocketSpeed = 20; // 0x2afd18, before acceleration.
    public const float RocketMaximumSpeed = 70; // 0x2afd30.
    public const float TaunterRange = 30; // Taunter PVar+8 default.
    public const float TaunterHalfAngle = .9599311f; // 0x3f75be0b.
    public const float TeslaRange = 20; // 0x2b8a48 / 0x2b8adc.
    public const double TeslaDamage = 2; // 0x2b99bc..2b99d8: upgrade + 2.
    public const uint TeslaDamageFlags = 0x00210000;
    public const double DroneDamage = 1; // 0x2ceb04 / 0x2ceb18, ordinary contact record.
    public const int WalloperSequence = 103; // enter_attack state0x20: hero_anim_set(0x67,4,-1).
    public const int WalloperDurationTicks = 68; // 17 source frames at rate1/4.
    public const int WalloperLungeFrom = 10, WalloperLungeTo = 18, WalloperContactTo = 22;
    public const float WalloperLungeSpeed = 18; // strike_update.
    public const int DecoyLifetime = 600; // 0x2c4a78, grounded phase timer.
    public const float DecoyInitialScale = .5f, DecoyGrowthPerTick = .02f; // 0x2c366c / 0x2c4688.
    public const float DoomCanisterHeldScale = .25f, DoomCanisterThrownScale = .5f; // 0x2c89e8 states1/2.
    public const float DoomCanisterGrowth = .05f;
    public const float RynoRocketScale = .25f; // 0x2ccf70..2ccf9c constructor.

    /// <summary>
    /// Native upper-body slot at item table+0x28. The Pyrocitor has no
    /// authored moving slot. Its host-only stabilized hold is composed separately
    /// from source51 instead of pretending that the Blaster's 57 belongs to it.
    /// </summary>
    // Source51 belongs to active Pyrocitor firing. The retail Pyrocitor row
    // provides no passive upper-body slot. As a host-only carry fallback, use
    // source54's two-arm supported forward pose (Devastator hold, frame0).
    // Unlike neutral0 and Blaster56, its opposite hand supports the Pyrocitor
    // mesh and it keeps the mesh clear of the floor.
    public const int PyrocitorReadySequence = 54;
    public const int PyrocitorHoldSequence = 51;
    public const double PyrocitorFirePhaseSeconds = 0.5; // source51 is five frames × 1/15 s; fire at its final frame.

    /// <summary>Host-only two-handed ready stance until the trigger is held.</summary>
    public static int PyrocitorBodySequence(bool firing, bool moving) =>
        firing ? PyrocitorHoldSequence : PyrocitorReadySequence;

    public static int MovingBodySequence(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.Pyrocitor => -1, // native moving upper-body slot absent
        Rac1WeaponId.Blaster or Rac1WeaponId.MorphORay => 57,
        Rac1WeaponId.Devastator => 73,
        _ => -1,
    };

    public static int FireTicks(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.Devastator => 35, // 0x2b2d48 -> PVar+0.
        Rac1WeaponId.DecoyGlove => 20, // 0x2d29c4 -> PVar+54.
        Rac1WeaponId.Ryno => (RynoMissilesPerUse - 1) * RynoMissileInterval + 60, // 0x2ccd20 follows the last missile.
        Rac1WeaponId.MorphORay => 1,
        Rac1WeaponId.Taunter => 4, // idle taunt query phase.
        Rac1WeaponId.Walloper => WalloperDurationTicks,
        Rac1WeaponId.VisibombGun => 1, // one live controlled missile, host admission.
        Rac1WeaponId.DroneDevice => 1, // one press fills the six-agent formation.
        Rac1WeaponId.TeslaClaw => 10, // host damage scheduler; full dual timers pending.
        _ => throw new ArgumentOutOfRangeException(nameof(weapon)),
    };

    public static bool IsNew(Rac1WeaponId weapon) => weapon is Rac1WeaponId.Devastator or Rac1WeaponId.VisibombGun or
        Rac1WeaponId.Taunter or Rac1WeaponId.Walloper or Rac1WeaponId.TeslaClaw or Rac1WeaponId.MorphORay or
        Rac1WeaponId.Ryno or Rac1WeaponId.DroneDevice or Rac1WeaponId.DecoyGlove;

    public static bool PressOnly(Rac1WeaponId weapon) => weapon is Rac1WeaponId.DroneDevice or Rac1WeaponId.VisibombGun;

    public static int HandFireSequence(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.Blaster => 4,
        Rac1WeaponId.SuckCannon => 5,
        Rac1WeaponId.Devastator or Rac1WeaponId.VisibombGun or Rac1WeaponId.Walloper or
            Rac1WeaponId.MorphORay or Rac1WeaponId.Taunter => 3,
        _ => 1,
    };

    public static bool AdmitsDamage(Rac1GameplayDamageEvent damage)
    {
        if (damage.NativeMarker is not null || damage.NativeDamage <= 0 || !double.IsFinite(damage.NativeDamage)) return false;
        if (damage.Source.Kind == Rac1GameplayEntityKind.PlayerEquipment &&
            damage.Owner?.RuntimeEntity == Rac1GameplayEntityRef.Player)
        {
            return (damage.Source.NativeClassId, damage.Source.RuntimeId, damage.NativeDamageFlags, damage.NativeDamage) is
                (157, 11, 0x830000, 3) or (163, 13, 0x830000, 6) or (180, 18, 0x30000, 3) or
                (177, 19, TeslaDamageFlags, TeslaDamage) or (483, 24, 0x10000, DroneDamage) or (454, 23, 0x830000, 3) or (175, 14, 0x10000, 1);
        }
        return false;
    }
}

/// <summary>Native seven-shot RYNO burst and rotating nine-barrel index.</summary>
public sealed class Rac1RynoSalvoSession
{
    private int _wait, _barrel;
    public int Remaining { get; private set; }
    public bool Begin()
    {
        if (Remaining != 0) return false;
        Remaining = Rac1ArsenalRules.RynoMissilesPerUse; _wait = 0; return true;
    }
    public int? Step(bool enabled = true)
    {
        if (!enabled) { Remaining = 0; return null; }
        if (Remaining == 0 || --_wait > 0) return null;
        Remaining--; _wait = Rac1ArsenalRules.RynoMissileInterval;
        _barrel = (_barrel + 1) % Rac1ArsenalRules.RynoBarrels;
        return _barrel;
    }
}

/// <summary>OBP choices for native routines whose full integration is still pending.</summary>
public static class Rac1ArsenalHostPolicy
{
    public const float RocketBlastRadius = 4;
    public const float VisibombBlastRadius = 4;
    public const float VisibombSpeed = 15;
    public const float GuidedTurnRate = 2.2f;
    public const int DecoyCapacity = 8;
    public const int MorphExposureTicks = 60;
    public const float MorphRange = 20;
    public const float DroneOrbitRadius = 1.5f;
    public const float DroneInterceptRange = 7;
    public const float DroneSpeed = 16;
}

/// <summary>Engine-independent projectile steering at 60 Hz, with no target through walls supplied by the host.</summary>
public sealed class Rac1ArsenalProjectile
{
    public Vector3 Position { get; private set; }
    public Vector3 Direction { get; private set; }
    public float Speed { get; private set; }
    public int Age { get; private set; }
    public int Lifetime { get; }

    public Rac1ArsenalProjectile(Vector3 position, Vector3 direction, float speed, int lifetime)
    {
        if (!Finite(position) || !Finite(direction) || direction.LengthSquared() < 1e-8f ||
            !float.IsFinite(speed) || speed <= 0 || lifetime <= 0) throw new ArgumentOutOfRangeException(nameof(direction));
        Position = position; Direction = Vector3.Normalize(direction); Speed = speed; Lifetime = lifetime;
    }

    public bool Step(Vector3? target = null, float turnRadiansPerSecond = 0, float acceleration = 0, float maximumSpeed = float.MaxValue)
    {
        if (target is { } point && Finite(point) && Vector3.DistanceSquared(point, Position) > 1e-8f)
        {
            var desired = Vector3.Normalize(point - Position);
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Direction, desired), -1, 1));
            if (angle > 1e-5f && turnRadiansPerSecond > 0)
            {
                float fraction = Math.Min(1, turnRadiansPerSecond / (60 * angle));
                var axis = Vector3.Cross(Direction, desired);
                if (axis.LengthSquared() < 1e-8f) axis = Vector3.Cross(Direction, Math.Abs(Direction.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX);
                Direction = Vector3.Normalize(Vector3.Transform(Direction, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle * fraction)));
            }
        }
        Speed = Math.Min(maximumSpeed, Speed + acceleration / 60);
        Position += Direction * (Speed / 60);
        return ++Age >= Lifetime;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
