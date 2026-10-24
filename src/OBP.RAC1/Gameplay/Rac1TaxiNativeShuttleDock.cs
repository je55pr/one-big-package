namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail Kerwan class-1012 state 0/1 side selection (L3:0x002dccc0).
/// PVar +0x80 chooses the startup path from +0x90/+0x94. State 1
/// compares the hero and taxi to both path first points and uses
/// the inverted closer-path index when selecting a reposition path.
/// This is route-selection evidence, not permission to dismount on
/// an unverified Godot collision surface.
/// </summary>
public sealed class Rac1TaxiNativeShuttleDock
{
    public Rac1TaxiNativeShuttleDock(
        IReadOnlyList<Rac1TaxiPoint> sideZeroPath,
        IReadOnlyList<Rac1TaxiPoint> sideOnePath,
        int initialSide,
        int optionalCuboidSelector)
    {
        ArgumentNullException.ThrowIfNull(sideZeroPath);
        ArgumentNullException.ThrowIfNull(sideOnePath);
        if (sideZeroPath.Count < 2 || sideOnePath.Count < 2 ||
            initialSide is not (0 or 1) || optionalCuboidSelector < -1 ||
            !sideZeroPath.All(IsFinite) || !sideOnePath.All(IsFinite))
            throw new ArgumentException("Invalid native two-sided shuttle paths or selectors.");

        SideZeroPath = sideZeroPath;
        SideOnePath = sideOnePath;
        InitialSide = initialSide;
        OptionalCuboidSelector = optionalCuboidSelector;
    }

    public IReadOnlyList<Rac1TaxiPoint> SideZeroPath { get; }
    public IReadOnlyList<Rac1TaxiPoint> SideOnePath { get; }
    public int InitialSide { get; }
    public int OptionalCuboidSelector { get; }
    public Rac1TaxiPoint NativeInitialWaypoint =>
        (InitialSide == 0 ? SideZeroPath : SideOnePath)[0];

    /// <summary>
    /// Returns the branch variable s4/s7 in the native state-1 updater:
    /// 1 if nearer the first point of side-zero's path, otherwise 0.
    /// The inverted value is intentional: the selected route begins
    /// at the *other* end when the cab needs to reposition to Ratchet.
    /// </summary>
    public int? OppositeNearestPathSide(Rac1TaxiPoint position)
    {
        if (!IsFinite(position)) return null;
        return position.DistanceSquared(SideZeroPath[0]) <
            position.DistanceSquared(SideOnePath[0]) ? 1 : 0;
    }

    /// <summary>
    /// The native state-1 no-interaction branch enters state 3 when
    /// Ratchet and the cab have different nearest-end selectors.
    /// HUD focus/input and optional cuboid admission are separate.
    /// </summary>
    public int? RepositionSide(Rac1TaxiPoint hero, Rac1TaxiPoint taxi)
    {
        int? heroSide = OppositeNearestPathSide(hero);
        int? taxiSide = OppositeNearestPathSide(taxi);
        return heroSide.HasValue && taxiSide.HasValue && heroSide != taxiSide
            ? heroSide : null;
    }

    private static bool IsFinite(Rac1TaxiPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
