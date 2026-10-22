namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail class-816/1012 waypoint seek at normalized NTSC-U 60 Hz.
/// Kerwan level-3 updater helper 0x002d3918 advances the native path
/// index when the current point is within 2 * (18 / 60), then moves
/// toward that point by a persistent speed driven by seek_1d (0x25b970).
/// Translation omits yaw, sound and native attach-frame side effects.
/// </summary>
public sealed class Rac1TaxiNativeMotion
{
    public const int NativeFramesPerSecond = 60;
    public const double MaxUnitsPerSecond = 18d;
    public const double AccelerationUnitsPerSecondSquared = 4.5d;
    public const double WaypointThreshold = 2d * MaxUnitsPerSecond / NativeFramesPerSecond;
    private const double Frame = 1d / NativeFramesPerSecond;
    private const double Acceleration = AccelerationUnitsPerSecondSquared /
        (NativeFramesPerSecond * NativeFramesPerSecond);
    private const double MaximumStep = MaxUnitsPerSecond / NativeFramesPerSecond;

    private readonly IReadOnlyList<Rac1TaxiPoint> _path;
    private readonly double[] _lengths;
    private readonly double[] _distanceAtWaypoint;
    private readonly double _totalLength;
    private int _waypoint;
    private double _velocity;
    private double _remainder;
    private double _distanceAlongPath;

    public Rac1TaxiNativeMotion(IReadOnlyList<Rac1TaxiPoint> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2 || path.Any(point =>
                !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) ||
                !double.IsFinite(point.Z)))
            throw new ArgumentException("Native taxi motion needs at least two finite waypoints.", nameof(path));

        _path = path;
        _lengths = new double[path.Count];
        _distanceAtWaypoint = new double[path.Count];
        for (int i = 1; i < path.Count; i++)
        {
            _lengths[i] = Math.Sqrt(path[i - 1].DistanceSquared(path[i]));
            _distanceAtWaypoint[i] = _distanceAtWaypoint[i - 1] + _lengths[i];
        }
        _totalLength = _distanceAtWaypoint[^1];
        if (_totalLength <= 0)
            throw new ArgumentException("Native taxi path must traverse a nonzero distance.", nameof(path));

        Position = path[0];
    }

    public Rac1TaxiPoint Position { get; private set; }
    public bool Arrived { get; private set; }
    public int WaypointIndex => _waypoint;
    public double SpeedPerFrame => _velocity;
    public double Progress => Arrived ? 1d : Math.Clamp(_distanceAlongPath / _totalLength, 0, 1);

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Arrived || seconds == 0)
            return;

        _remainder += seconds;
        // Keep step semantics independent of host render frames.
        // 30 Hz and 120 Hz hosts execute the same 60 Hz retail updates.
        while (_remainder + 1e-10 >= Frame && !Arrived)
        {
            _remainder = Math.Max(0, _remainder - Frame);
            TickNativeFrame();
        }
    }

    private void TickNativeFrame()
    {
        var destination = _path[_waypoint];
        double distance = Math.Sqrt(Position.DistanceSquared(destination));
        if (distance < WaypointThreshold)
        {
            _distanceAlongPath += _lengths[_waypoint];
            _waypoint++;
            if (_waypoint >= _path.Count)
            {
                Arrived = true;
                // Landing/dismount stays on the authored path endpoint;
                // retail transition itself occurs within a 0.6-unit radius.
                Position = _path[^1];
                return;
            }
            destination = _path[_waypoint];
            distance = Math.Sqrt(Position.DistanceSquared(destination));
        }

        // Original seek_1d_v uses a zeroed local position each frame,
        // writes its accelerated step to PVar+0x60 and clamps to target.
        // Reverse direction and negative distances are not used here.
        double stop = _velocity * _velocity / (2 * Acceleration);
        if (distance < stop)
        {
            double deceleration = stop < distance + Math.Abs(_velocity)
                ? Acceleration : Acceleration * 1.1d;
            _velocity = Math.Max(0, _velocity - deceleration);
        }
        else
        {
            double desired = Math.Min(MaximumStep, Math.Sqrt(2 * Acceleration * distance));
            _velocity = Math.Min(desired, _velocity + Acceleration);
        }
        double step = Math.Min(distance, _velocity);
        if (distance > 0 && step > 0)
            Position = Rac1TaxiPoint.Lerp(Position, destination, step / distance);

        double segmentLength = _lengths[_waypoint];
        if (segmentLength > 0)
        {
            double partial = Math.Clamp(segmentLength -
                Math.Sqrt(Position.DistanceSquared(destination)), 0, segmentLength);
            _distanceAlongPath = Math.Min(_totalLength,
                _distanceAtWaypoint[_waypoint - 1] + partial);
        }
    }
}
