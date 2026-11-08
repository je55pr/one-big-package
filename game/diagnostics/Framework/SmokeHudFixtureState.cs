using OBP.Core;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>Immutable, engine-free synthetic HUD observations for the smoke fixture.</summary>
internal static class SmokeHudFixtureState
{
    internal static HudPresentationState Create()
    {
        return new(
            Health: new HudHealth(3, 6, "nanotech", HudLifeState.Alive),
            Bolts: new HudCurrency("bolts", 12_450),
            CurrentWeapon: new HudWeapon(
                Weapon: new SourceWeaponKey(ObpSourceGame.Rac1, 23),
                PresentationKey: "rac1.weapon.ryno",
                NameKey: "weapon.ryno",
                Ammo: new HudAmmo(26, 50)),
            ContextPrompt: new HudContextPrompt(
                PromptId: "hud-fixture-console",
                ActionId: "interact",
                MessageKey: "Activate test console",
                Progress: 0.42));
    }
}
