using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Presentation;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Thin R&C1-to-runtime HUD projection. It observes existing gameplay owners
/// without moving health, inventory, ammo or pickup rules into presentation.
/// </summary>
public static class Rac1HudProjection
{
    public const string NanotechUnitKey = "nanotech";
    public const string BoltsResourceKey = "bolts";
    public const string WrenchPresentationKey = "rac1.weapon.wrench";
    public const string BombGlovePresentationKey = "rac1.weapon.bomb-glove";
    public const string BombGloveAmmoResourceKey = "rac1.weapon.bomb-glove.ammo";
    public const string WrenchNameKey = "weapon.wrench";
    public const string BombGloveNameKey = "weapon.bomb-glove";

    public static HudPresentationState Capture(
        Rac1RatchetNanotechSession nanotech,
        Rac1WeaponInventory weapons)
    {
        ArgumentNullException.ThrowIfNull(nanotech);
        ArgumentNullException.ThrowIfNull(weapons);

        Rac1RatchetNanotechSnapshot health = nanotech.Probe();
        var hudHealth = new HudHealth(
            Current: health.Nanotech,
            Capacity: null,
            UnitKey: NanotechUnitKey,
            LifeState: health.LifeState switch
            {
                Rac1RatchetLifeState.Alive => HudLifeState.Alive,
                Rac1RatchetLifeState.Dead => HudLifeState.Dead,
                _ => null,
            });

        HudWeapon weapon = weapons.Equipped switch
        {
            Rac1WeaponId.Wrench => new HudWeapon(
                PresentationKey: WrenchPresentationKey,
                NameKey: WrenchNameKey,
                Ammo: null),
            Rac1WeaponId.FirstRanged => new HudWeapon(
                PresentationKey: BombGlovePresentationKey,
                NameKey: BombGloveNameKey,
                Ammo: new HudAmmo(
                    Current: weapons.FirstRangedAmmo,
                    Capacity: Rac1BombGlove.MaxAmmo)),
            _ => throw new InvalidOperationException(
                $"Unsupported R&C1 HUD weapon {weapons.Equipped}."),
        };
        // The current crate-session counter is bounded pickup telemetry, not a
        // reconstructed player wallet, so normal HUD currency stays absent.
        return new HudPresentationState(
            Health: hudHealth,
            Bolts: null,
            CurrentWeapon: weapon,
            ContextPrompt: null);
    }

    public static HudFeedbackDraft DamageFeedback(
        Rac1RatchetNanotechSnapshot before,
        Rac1RatchetNanotechSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        int delta = checked(after.Nanotech - before.Nanotech);
        if (delta >= 0)
            throw new ArgumentException("Damage feedback requires a Nanotech decrease.", nameof(after));

        return new HudFeedbackDraft(
            Kind: HudFeedbackKind.Damage,
            ResourceKey: NanotechUnitKey,
            Delta: delta);
    }
    public static HudFeedbackDraft CollectedBoltFeedback(int collectedValue)
    {
        if (collectedValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(collectedValue));

        return new HudFeedbackDraft(
            Kind: HudFeedbackKind.Pickup,
            ResourceKey: BoltsResourceKey,
            Delta: collectedValue);
    }

    public static HudFeedbackDraft BombGloveAcquiredFeedback(int ammoGranted)
    {
        if (ammoGranted < 0)
            throw new ArgumentOutOfRangeException(nameof(ammoGranted));

        return new HudFeedbackDraft(
            Kind: HudFeedbackKind.Pickup,
            ResourceKey: ammoGranted > 0 ? BombGloveAmmoResourceKey : null,
            Delta: ammoGranted > 0 ? ammoGranted : null,
            SubjectKey: BombGloveNameKey);
    }

    public static HudFeedbackDraft BombGloveAmmoPickupFeedback(int ammoGranted)
    {
        if (ammoGranted <= 0)
            throw new ArgumentOutOfRangeException(nameof(ammoGranted));

        return new HudFeedbackDraft(
            Kind: HudFeedbackKind.Pickup,
            ResourceKey: BombGloveAmmoResourceKey,
            Delta: ammoGranted,
            SubjectKey: BombGloveNameKey);
    }
}
