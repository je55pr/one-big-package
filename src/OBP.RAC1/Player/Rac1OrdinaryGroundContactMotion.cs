using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// Recovered ordinary R&C1 pre-contact vertical motion.
/// </summary>
public static class Rac1OrdinaryGroundContactMotion
{
    public const double NativeTickScalar = 1d / 3600d;
    public const double GroundDownwardRequestPerTick = 54d * NativeTickScalar;
    public const double EdgeFallAccelerationPerTick = 25d * NativeTickScalar;

    public static double ResolvePreContactVertical(
        Rac1RatchetMovementController.StepResult movement,
        PlayerContactFacts priorContact)
    {
        // Retail state 2 uses the prior contact result when constructing the
        // next movement vector. A supported ordinary tick receives a
        // 54/3600 downward adhesion step before the collision query. If the
        // support ends, that step becomes the first actual edge displacement;
        // the controller then continues the recovered 25/3600 edge recurrence.
        if (priorContact.IsGrounded &&
            movement.Phase == Rac1RatchetMovementPhase.Grounded)
            return movement.Vertical - GroundDownwardRequestPerTick;

        return movement.Vertical;
    }
}