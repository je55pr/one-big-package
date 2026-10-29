namespace OBP.RAC1.Gameplay;

public readonly record struct Rac1WrenchHostPoint(double X, double Y, double Z);

public readonly record struct Rac1WrenchHostDirection(double X, double Y, double Z);

/// <summary>
/// OBP host-policy collision admission for the ordinary wrench presentation.
/// None of these dimensions are claimed as retail wrench geometry. Admission is
/// planar because imported Moby root height is not a recovered contact anchor.
/// The policy exists only so a visually obvious swing can feed separately
/// recovered victim consequences while retail contact geometry remains unresolved.
/// </summary>
public static class Rac1WrenchHostContactPolicy
{
    public const double ForwardReach = 2.6d;
    public const double SweepRadius = 0.9d;
    public const double TargetRadius = 0.65d;
    public const double RearGrace = 0.25d;

    public static bool Admits(
        Rac1WrenchHostPoint root,
        Rac1WrenchHostDirection forward,
        Rac1WrenchHostPoint targetCenter,
        double targetRadius = TargetRadius)
    {
        if (!double.IsFinite(targetRadius) || targetRadius < 0d)
            throw new ArgumentOutOfRangeException(nameof(targetRadius));
        // Godot's imported Moby root height is not a recovered retail wrench
        // contact anchor. Admit in the walk plane only, using host X/Z, so a
        // visually adjacent target is not rejected merely because its authored
        // origin sits below or above the walkable surface.
        double length = Math.Sqrt(
            (forward.X * forward.X) +
            (forward.Z * forward.Z));
        if (!(length > 0d) || !double.IsFinite(length))
            throw new ArgumentException(
                "Host wrench forward direction must have a finite planar axis.",
                nameof(forward));

        double fx = forward.X / length;
        double fz = forward.Z / length;
        double dx = targetCenter.X - root.X;
        double dz = targetCenter.Z - root.Z;
        double along = (dx * fx) + (dz * fz);
        if (along < -RearGrace || along > ForwardReach + targetRadius)
            return false;

        double clampedAlong = Math.Clamp(along, 0d, ForwardReach);
        double cx = root.X + (fx * clampedAlong);
        double cz = root.Z + (fz * clampedAlong);
        double ox = targetCenter.X - cx;
        double oz = targetCenter.Z - cz;
        double distanceSquared = (ox * ox) + (oz * oz);
        double admittedRadius = SweepRadius + targetRadius;
        return distanceSquared <= admittedRadius * admittedRadius;
    }
}
