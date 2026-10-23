using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Conservative Godot-only vertical melee-overlap guard while the retail
/// Amoeboid/player-body collision service is not reconstructed. Both
/// foot positions are expressed in native coordinates (Z is up); heights
/// come from the actual foot-aligned Godot contact capsules.
/// Line of sight and native lunge range are independent admissions.
/// </summary>
public static class Rac1AmoeboidHostMeleeContact
{
    public static bool VerticalCapsulesOverlap(Vector3 attackerFeet, float attackerHeight,
        Vector3 targetFeet, float targetHeight)
    {
        if (!float.IsFinite(attackerFeet.Z) || !float.IsFinite(targetFeet.Z) ||
            !float.IsFinite(attackerHeight) || !float.IsFinite(targetHeight) ||
            attackerHeight <= 0f || targetHeight <= 0f)
            return false;

        // These host capsules start at their actors' feet. A distant
        // airborne target or one on an exposed upper/lower ledge cannot
        // take a ground-level lunge merely because the LOS ray is clear.
        return attackerFeet.Z < targetFeet.Z + targetHeight &&
            targetFeet.Z < attackerFeet.Z + attackerHeight;
    }
}
