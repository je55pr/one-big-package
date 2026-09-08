namespace OBP.RAC2.Gameplay;

/// <summary>
/// Proven Going Commando collision-damage fields shared by the compact input
/// descriptor and the owned per-Moby damage record. Retail GC establishes the
/// dataflow; later public PS2-era structures only corroborate the field names.
/// </summary>
public readonly record struct GcCollisionDamage(
    uint DamageFlags,
    byte DamageClass,
    byte DamageStrength,
    ushort DamageIndex,
    float DamageHp,
    uint Flags);

/// <summary>
/// Exact retail damage tuple constructed by GC player state 20 on Oozla.
/// The semantic state name is intentionally not encoded here: later public UYA
/// code calls state 20 JumpAttack, but GC retail is authoritative for this API.
/// </summary>
public static class GcPlayerAttackDamage
{
    public const int State20Id = 20;

    public static readonly GcCollisionDamage State20 = new(
        DamageFlags: 0x00010000,
        DamageClass: 0,
        DamageStrength: 1,
        DamageIndex: 71,
        DamageHp: 2.0f,
        Flags: 1);
}
