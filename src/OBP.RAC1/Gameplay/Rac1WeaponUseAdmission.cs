using OBP.RAC1.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Why a semantic R&C1 weapon-use request was not admitted this native update.
/// Numeric cadence remains weapon-specific; this enum is only the shared result vocabulary.
/// </summary>
public enum Rac1WeaponUseRejection
{
    None = 0,
    NotEquipped,
    NoAmmo,
    CadenceBlocked,
    SpawnNotReady,
}

/// <summary>
/// Engine-neutral result of one requested R&C1 weapon use. An accepted use may
/// expose a recovered native player selector without making animation playback
/// authoritative for combat timing.
/// </summary>
public sealed record Rac1WeaponUseAdmission(
    Rac1WeaponId WeaponId,
    bool Accepted,
    Rac1WeaponUseRejection Rejection,
    int? AmmoBefore,
    int? AmmoAfter,
    int? NativePlayerSequenceId)
{
    public static Rac1WeaponUseAdmission Reject(
        Rac1WeaponId weaponId,
        Rac1WeaponUseRejection rejection,
        int? ammo = null) =>
        new(weaponId, false, rejection, ammo, ammo, null);

    public static Rac1WeaponUseAdmission AcceptWrench() =>
        new(
            Rac1WeaponId.Wrench,
            true,
            Rac1WeaponUseRejection.None,
            null,
            null,
            Rac1RatchetSequenceSelection.WrenchAttackSequenceId);

    public static Rac1WeaponUseAdmission AcceptFirstRanged(int ammoBefore, int ammoAfter) =>
        new(
            Rac1WeaponId.FirstRanged,
            true,
            Rac1WeaponUseRejection.None,
            ammoBefore,
            ammoAfter,
            Rac1RatchetSequenceSelection.FirstRangedFireSequenceId);
}

/// <summary>
/// Native spawn ownership facts that are safe to share without assuming a
/// universal R&C1 projectile layout.
/// </summary>
public sealed record Rac1WeaponSpawnOwnership(
    int OwnerNativeClassId,
    int SpawnedNativeClassId,
    int SpawnedCreationNativeState,
    int SpawnedLaunchNativeState,
    int SpawnedPvarOwnerPointerOffset,
    int OwnerPvarStagedObjectOffset,
    bool UsesDedicatedWeaponLaunchFrame);
