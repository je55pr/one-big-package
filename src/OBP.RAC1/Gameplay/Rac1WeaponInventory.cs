namespace OBP.RAC1.Gameplay;

/// <summary>
/// The two retail gadget ids promoted by the current RAC1 playable slice.
/// Gadget id 10 remains deliberately named by role until the ranged-weapon lane
/// owns the presentation/name contract.
/// </summary>
public enum Rac1WeaponId
{
    Wrench = 8,
    FirstRanged = 10,
}

/// <summary>
/// Minimal RAC1-owned weapon state for the wrench plus the first ranged weapon.
/// This is intentionally not a trilogy-wide inventory or gadget framework.
/// </summary>
public sealed class Rac1WeaponInventory
{
    public Rac1WeaponInventory(
        bool ownsFirstRanged,
        Rac1WeaponId equipped = Rac1WeaponId.Wrench,
        int firstRangedAmmo = 0)
    {
        if (firstRangedAmmo < 0)
            throw new ArgumentOutOfRangeException(nameof(firstRangedAmmo));
        if (!IsSupported(equipped))
            throw new ArgumentOutOfRangeException(nameof(equipped));
        if (equipped == Rac1WeaponId.FirstRanged && !ownsFirstRanged)
            throw new ArgumentException("The equipped RAC1 weapon must be owned.", nameof(equipped));

        OwnsFirstRanged = ownsFirstRanged;
        Equipped = equipped;
        FirstRangedAmmo = firstRangedAmmo;
    }

    public bool OwnsFirstRanged { get; }
    public Rac1WeaponId Equipped { get; private set; }
    public int FirstRangedAmmo { get; private set; }

    public bool Owns(Rac1WeaponId weapon) => weapon switch
    {
        Rac1WeaponId.Wrench => true,
        Rac1WeaponId.FirstRanged => OwnsFirstRanged,
        _ => false,
    };

    public bool TryEquip(Rac1WeaponId weapon)
    {
        if (!IsSupported(weapon) || !Owns(weapon)) return false;
        Equipped = weapon;
        return true;
    }

    /// <summary>
    /// Accept one use of the equipped slice weapon. Wrench use is ammo-free.
    /// The ranged path consumes exactly one round only when a round exists.
    /// Damage/projectile consequences belong to their separate RAC1 controllers.
    /// </summary>
    public bool TryUseEquipped()
    {
        if (Equipped == Rac1WeaponId.Wrench) return true;
        if (FirstRangedAmmo == 0) return false;

        FirstRangedAmmo--;
        return true;
    }

    private static bool IsSupported(Rac1WeaponId weapon) =>
        weapon is Rac1WeaponId.Wrench or Rac1WeaponId.FirstRanged;
}
