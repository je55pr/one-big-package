namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-816 Kerwan post-ride departure gates from state 6 of
/// 0x002d33e0: a selected departure spline waits for 15 native units
/// in 3D, while the -1 (pathless) selector waits for 5 planar units
/// and returns to its idle path-selection state without flying.
/// </summary>
public static class Rac1TaxiNativeDepartureClearance
{
    public const double WithDeparturePathRadius = 15d;
    public const double PathlessRadius = 5d;

    public static bool Cleared(
        Rac1TaxiPoint taxi, Rac1TaxiPoint hero, int? departurePathSelector)
    {
        if (!IsFinite(taxi) || !IsFinite(hero))
            return false;

        double x = taxi.X - hero.X;
        double z = taxi.Z - hero.Z;
        double squared = x * x + z * z;
        if (departurePathSelector == -1)
            return squared > PathlessRadius * PathlessRadius;
        double y = taxi.Y - hero.Y;
        return squared + y * y > WithDeparturePathRadius * WithDeparturePathRadius;
    }

    private static bool IsFinite(Rac1TaxiPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
