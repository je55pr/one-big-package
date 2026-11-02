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
                PresentationKey: "hud.fixture.vector-blaster",
                NameKey: "Vector Blaster",
                Ammo: new HudAmmo(7, 12)),
            ContextPrompt: new HudContextPrompt(
                PromptId: "hud-fixture-console",
                ActionId: "interact",
                MessageKey: "Activate test console",
                Progress: 0.42));
    }
}
