using OBP.RAC1.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

public static class Rac1CombatAssertions
{
    public static float RadToDeg(float value) => value * (180f / MathF.PI);
    public static void HudWeapon(HudWeapon? weapon, string expectedPresentationKey, int? expectedAmmo)
    {
        if (weapon is null) throw new InvalidOperationException("R&C1 HUD has no current weapon.");
        if (weapon.PresentationKey != expectedPresentationKey)
            throw new InvalidOperationException($"R&C1 HUD weapon was '{weapon.PresentationKey}', expected '{expectedPresentationKey}'.");
        if (expectedAmmo is null)
        {
            if (weapon.Ammo is not null) throw new InvalidOperationException("Wrench HUD unexpectedly exposed ammo.");
            return;
        }
        if (weapon.Ammo?.Current != expectedAmmo || weapon.Ammo.Capacity != Rac1BombGlove.MaxAmmo)
            throw new InvalidOperationException($"Bomb Glove HUD ammo was {weapon.Ammo?.Current}/{weapon.Ammo?.Capacity}, expected {expectedAmmo}/{Rac1BombGlove.MaxAmmo}.");
    }
    public static void Wrench(Rac1CombatPresentationDiagnostic value, bool visible, string phase)
    {
        if (!value.WrenchPresent) throw new InvalidOperationException($"R&C1 Wrench presentation disappeared during {phase}.");
        if (value.WrenchVisible != visible) throw new InvalidOperationException($"R&C1 Wrench visibility during {phase} was {value.WrenchVisible}, expected {visible}.");
        if (!value.WrenchParentedToAvatar) throw new InvalidOperationException($"R&C1 Wrench was not parented to the animated Ratchet view during {phase}.");
    }
}
