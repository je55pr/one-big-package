namespace OBP.RAC1.Gameplay;

/// <summary>
/// Class-1012 Kerwan non-passenger state 1 -> 3 -> 1 route transitions.
/// Runs only when the caller admits the *unoccupied* native reposition
/// branch. HUD focus, player input, camera and passenger attachment are
/// separate original branches, and are deliberately not inferred here.
/// </summary>
public sealed class Rac1TaxiNativeShuttleTraffic
{
    public const int IdleState = 1;
    public const int TravellingState = 3;
    public const double NativeArrivalFocusDelay = 15d / 60d;

    private readonly Rac1TaxiNativeShuttleDock _dock;
    private Rac1TaxiNativeMotion? _motion;

    public Rac1TaxiNativeShuttleTraffic(Rac1TaxiNativeShuttleDock dock)
    {
        _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        Position = dock.NativeInitialWaypoint;
        CurrentSide = dock.InitialSide;
    }

    public Rac1TaxiPoint Position { get; private set; }
    public int CurrentSide { get; private set; }
    public int NativeState => _motion is null ? IdleState : TravellingState;
    public double FocusDelayRemaining { get; private set; }

    /// <summary>
    /// Native state 1 compares distances from cab/player to each authored
    /// endpoint. If they favour opposite ends, state 3 selects the path
    /// starting at the cab's end, places the cab at that path's first
    /// waypoint and begins seeking on the following updater tick.
    /// Do not call while the original player/focus admission branch owns
    /// the cab.
    /// </summary>
    public bool TryStartUnoccupiedReposition(Rac1TaxiPoint hero)
    {
        if (_motion is not null)
            return false;
        int? newSide = _dock.RepositionSide(hero, Position);
        if (newSide is not (0 or 1))
            return false;

        var route = newSide == 0 ? _dock.SideZeroPath : _dock.SideOnePath;
        var motion = new Rac1TaxiNativeMotion(route);
        _motion = motion;
        CurrentSide = newSide.Value;
        Position = motion.Position;
        FocusDelayRemaining = 0;
        return true;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        if (_motion is not { } motion)
        {
            FocusDelayRemaining = Math.Max(0, FocusDelayRemaining - seconds);
            return;
        }

        motion.Advance(seconds);
        Position = motion.Position;
        if (motion.Arrived)
        {
            _motion = null;
            FocusDelayRemaining = NativeArrivalFocusDelay;
        }
    }

    public void Reset()
    {
        _motion = null;
        CurrentSide = _dock.InitialSide;
        Position = _dock.NativeInitialWaypoint;
        FocusDelayRemaining = 0;
    }
}
