using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Deliberate OBP adapter, never native cross-game behavior. Original damage
/// amounts pass through; foreign flag bits do not masquerade as R&C1 flags.
/// Only admitted R&C1 crates, toads and robots receive translated envelopes.
/// R&C1 controllers still own immunity, health, reactions and rewards.
/// </summary>
public static class FusionDamagePolicy
{
    public const string Id = "rac1-veldin-original-damage-v1";
    public static bool Admits(WeaponTargetKey target) => target.SourceGame == ObpSourceGame.Rac1 && target.InstanceIndex >= 0 &&
        target.NativeClassId is Rac1ClassIds.WoodenCrate or Rac1ClassIds.NanotechCrate or Rac1ClassIds.AmmoCrate or Rac1ClassIds.VeldinToad or Rac1ClassIds.VeldinFlameRobot;

    /// <summary>Keep foreign queries within the admitted R&C1 families; native victim rules still decide each hit.</summary>
    public static bool AdmitsQueryTarget(SourceWeaponKey weapon, WeaponTargetKey target) =>
        weapon.SourceGame == ObpSourceGame.Rac1 || target.SourceGame != ObpSourceGame.Rac1 || Admits(target);

    public static Rac1GameplayDamageEvent? Translate(WeaponDamageIntent intent)
    {
        if (!Admits(intent.Target) || !float.IsFinite(intent.SourceDamage) || intent.SourceDamage <= 0 ||
            intent.Target.NativeClassId == Rac1ClassIds.VeldinToad && intent.SourceDamage < 1)
            return null;
        // R&C1 actors hosted in Fusion Lab still use R&C1 class-176 damage,
        // rather than laundering their own weapon through the foreign adapter.
        // Constrain the bridge to the genuine recovered flame envelope.
        if (intent.Shot.Weapon.SourceGame == ObpSourceGame.Rac1)
        {
            if (intent.Shot.Weapon.NativeItemId != (int)Rac1WeaponId.Pyrocitor ||
                intent.Kind != "native-flame" ||
                intent.SourceDamage != (float)Rac1WeaponProfiles.PyrocitorDamage ||
                intent.SourceFlags != Rac1WeaponProfiles.PyrocitorFlags) return null;
            return Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor,
                new(intent.Target.NativeClassId, intent.Target.InstanceIndex),
                Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags);
        }
        if (intent.Shot.Weapon != FusionWeaponSession.Lancer && intent.Shot.Weapon != FusionWeaponSession.ShockCannon)
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
