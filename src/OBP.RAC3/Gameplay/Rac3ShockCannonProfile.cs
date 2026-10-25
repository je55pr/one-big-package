using OBP.Core;
namespace OBP.RAC3.Gameplay;

/// <summary>Source-qualified identity; 43 alone is not a portable weapon ID.</summary>
public sealed record Rac3ShockCannonIdentity
{
    [System.Text.Json.Serialization.JsonConverter(typeof(SourceGameWireJsonConverter))] public ObpSourceGame SourceGame => ObpSourceGame.Rac3;
    public string SourceBuild => "rac3-ntscu-original";
    public int ResourceSlot => 39;
    public int ItemDefinitionIndex => 43;
    public string Key => $"{SourceIdentityWire.FormatSource(SourceGame)}/{SourceBuild}/item:{ItemDefinitionIndex}";
}

public sealed record Rac3ShockCannonUpgrade(
    int DefinitionIndex,
    int NativeLevel,
    int NameTextId,
    string Name,
    int PreviousDefinition,
    int NextDefinition,
    int HeldMobyClass,
    int AmmoCapacity);

/// <summary>
/// Original NTSC-U V5 only. Constants are native observations, not tuning.
/// See research/UYA_SHOCK_CANNON.md for addresses, gates and omitted paths.
/// </summary>
public static class Rac3ShockCannonProfile
{
    public static Rac3ShockCannonIdentity Identity { get; } = new();
    public const int HeldMobyClass = 0x1882;
    public const int BlasterHeldMobyClass = 0x1863;
    public const int ItemTableAddress = 0x002c5640;
    public const int ItemDefinitionStride = 0xf0;
    public const int AmmoCapacity = 50;
    public const int AmmoPickupAmount = 4;
    public const int ChargeUpdates = 79;
    public const int ChargedAmmoAttempts = 10;
    public const int PrimaryWindupUpdates = 5;
    public const int AlternateWindupUpdates = 2;
    public const int RecoveryUpdates = 20;
    public const int BeamUpdates = 30;
    public const int PrimaryAttackSequence = 49;
    public const int SecondarySequenceField = 65;
    public const int NativePlayerAction = 29;
    public const float BaseDamage = 5;
    public const float ChargedDamage = BaseDamage * 10;
    public const uint SpreadDamageFlags = 0x00410000;
    public const uint BeamDamageFlags = 0x00210000;
    public const byte DamageRecordKind = 3;
    public const byte DamageOwnerKind = 2;
    public const ushort BeamTargetModeMask = 0x4000;
    public const int RandomRayCount = 23;
    public const int FixedRayCount = 3;
    public const int BeamSampleCount = 20;
    public const float RayLength = 32;
    public const float RayEndpointExtension = 1.5f;
    public const float BeamSampleRadius = 1.5f;
    public const float BeamSampleSpacing = 2;

    private static readonly IReadOnlyList<Rac3ShockCannonUpgrade> upgrades = Array.AsReadOnly<Rac3ShockCannonUpgrade>(
    [
        new(39, 0, 0xaa0, "Shock Blaster", 0, 40, 0x1863, 30),
        new(40, 1, 0xa6c, "Shock Blaster V2", 39, 41, 0x1863, 35),
        new(41, 2, 0xa6d, "Shock Blaster V3", 40, 42, 0x1863, 40),
        new(42, 3, 0xa6e, "Shock Blaster V4", 41, 43, 0x1863, 40),
        new(43, 4, 0xa6f, "Shock Cannon", 42, 44, 0x1882, 50),
        new(44, 5, 0xdb7, "Mega Shock Cannon", 43, 45, 0x1882, 50),
        new(45, 6, 0xdb8, "Giga Shock Cannon", 44, 46, 0x1882, 55),
        new(46, 7, 0xdb9, "Omega Shock Cannon", 45, 0, 0x1882, 60),
    ]);

    public static IReadOnlyList<Rac3ShockCannonUpgrade> UpgradeFamily => upgrades;

    /// <summary>0x003870C8..0x0038713C, using distance from native ray origin.</summary>
    public static float RayDamage(float distance)
    {
        if (!float.IsFinite(distance) || distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        return BaseDamage * (distance < 10 ? 1 : distance < 14 ? .75f : distance < 20 ? .5f : .125f);
    }
}
