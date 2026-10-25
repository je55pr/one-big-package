using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

/// <summary>
/// Deliberate OBP adapter, never native cross-game behavior. Original damage
/// amounts pass through; foreign flag bits do not masquerade as R&C1 flags.
/// Only ordinary Veldin crates, toads and robots are admitted in this demo.
/// R&C1 controllers still own immunity, health, reactions and rewards.
/// </summary>
public static class FusionDamagePolicy
{
    public const string Id = "rac1-veldin-original-damage-v1";
    public static bool Admits(WeaponTargetKey target) => target.SourceGame == "rac1" && target.InstanceIndex >= 0 &&
        target.NativeClassId is 500 or 501 or 511 or 749 or 1440;

    /// <summary>Keep foreign queries within the admitted R&C1 families; native victim rules still decide each hit.</summary>
    public static bool AdmitsQueryTarget(SourceWeaponKey weapon, WeaponTargetKey target) =>
        weapon.SourceGame == "rac1" || target.SourceGame != "rac1" || Admits(target);

    public static Rac1GameplayDamageEvent? Translate(WeaponDamageIntent intent)
    {
        if (!Admits(intent.Target) || !float.IsFinite(intent.SourceDamage) || intent.SourceDamage <= 0 ||
            intent.Target.NativeClassId == 749 && intent.SourceDamage < 1 ||
            intent.Shot.Weapon != FusionWeaponSession.Lancer && intent.Shot.Weapon != FusionWeaponSession.ShockCannon)
            return null;
        // The source is a logical player action in this target-game envelope.
        // The separately retained neutral intent carries its real weapon/key;
        // class 1490/1882/3070 are never reinterpreted as R&C1 classes.
        return new(Rac1GameplayEntityRef.Player,
            Rac1GameplayEntityRef.Moby(new(intent.Target.NativeClassId, intent.Target.InstanceIndex)),
            intent.SourceDamage, nativeDamageFlags: 0x00010000,
            owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player), hostWeaponIntent: intent);
    }
}
