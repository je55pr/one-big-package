using OBP.Core;
namespace OBP.RAC2.Gameplay;

/// <summary>
/// Base Lancer facts from SCUS-97268 v1.01, using the loaded Oozla overlay.
/// These identities belong to GC; neither the Heavy Lancer nor campaign
/// ownership/upgrade rules are part of this profile. See research/GC_LANCER.md.
/// </summary>
public static class GcLancerProfile
{
    public const ObpSourceGame SourceGame = ObpSourceGame.Rac2;
    public const int WeaponIndex = 30;
    public const int NameTextId = 2677;
    public const int HeldMobyClass = 1490;
    public const int ProjectileMobyClass = 3070;
    public const int AmmoCapacity = 200;
    public const int AmmoPerShot = 1;

    public const int TicksPerSecond = 60;
    public const float TickSeconds = 1f / TicksPerSecond;
    public const float EquipDelaySeconds = 0.1f;
    public const float ShotsPerSecond = 7f;
    public const float FireIntervalSeconds = 1f / ShotsPerSecond;
    public const float ProjectileSpeed = 60f;
    public const float ProjectileRange = 20f;
    public const int ProjectileFlightTicks = 20;
    public const int ImpactEffectTicks = 6;
    public const int MuzzleJoint = 0;

    public const int HeldEquipSequence = 0;
    public const int HeldIdleSequence = 1;
    public const int HeldFireSequence = 2;
    public const int GroundedPlayerFireSequence = 56;
    public const int DefaultPlayerBaseSequence = 0;
    public const int PlayerFireTransitionFrames = 5;

    // Class-local sound ids, not indices into an arbitrary world's SBlk bank.
    public const int FireSound = 0;
    public const int EmptySound = 1;
    public const int ProjectileImpactSound = 0;

    public static GcCollisionDamage ProjectileDamage { get; } = new(
        DamageFlags: 0x00010000,
        DamageClass: 1,
        DamageStrength: 1,
        DamageIndex: ProjectileMobyClass,
        DamageHp: 0.5f,
        Flags: 1);

    /// <summary>
    /// The separate native near-muzzle overlap pass attributes its descriptor
    /// to the held weapon. This is evidence for a future host overlap adapter,
    /// not an extra hit automatically emitted by the portable session.
    /// </summary>
    public static GcCollisionDamage MuzzleOverlapDamage { get; } =
        ProjectileDamage with { DamageIndex = HeldMobyClass };
}
