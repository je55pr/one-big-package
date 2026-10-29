using Godot;
using OBP.RAC1.Gameplay;

namespace OBP.Game;

/// <summary>Godot contact input for ordinary R&C1 class-749 gravity; other families retain their native displacement.</summary>
internal static class Rac1EnemyContactMotion
{
    internal static Vector3 PrepareOrdinary(int nativeClass, bool grounded, bool reacting, Vector3 motion)
    {
        // Grounded class-749 gravity belongs to the support sweep after the wall slide.
        // Including it in MoveAndCollide can produce an inconsistent second floor contact
        // between fresh processes. Never edit launches, airborne motion or hit reactions.
        if (nativeClass == Rac1ClassIds.VeldinToad && grounded && !reacting && motion.Y < 0)
            motion.Y = 0;
        return motion;
    }
}
