namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original Ratchet scripted target states reached by hero_cam_target_set
/// (0x00233ee8). The taxi updater checks these via sub_00233f38
/// (0x00233f38), not via a fixed boarding animation timer.
/// Position, planar speed and animation completion come from the real
/// player controller; this state gate does not invent motion or collision.
/// </summary>
public sealed class Rac1TaxiHeroTargetSession
{
    public const int WalkingState = 0x65;
    public const int BrakingState = 0x66;
    public const int SettledState = 0x67;
    public const double TargetRadius = 0.2d;
    public const double FacingTolerance = Math.PI / 90d; // Native 0x3d0efa35 = 2 degrees.
    public const double StopSpeedPerFrame = 2.7d / 60d;

    public Rac1TaxiHeroTargetSession(Rac1TaxiPoint target, double targetYaw, int flag = 0,
        int movementMode = 0)
    {
        if (!IsFinite(target) || !double.IsFinite(targetYaw))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (movementMode is not (0 or 1 or 3))
            throw new ArgumentOutOfRangeException(nameof(movementMode));
        Target = target;
        TargetYaw = Wrap(targetYaw);
        Flag = flag;
        MovementMode = movementMode;
    }

    public Rac1TaxiPoint Target { get; private set; }
    public double TargetYaw { get; private set; }
    public int Flag { get; private set; }
    public int MovementMode { get; }
    public int NativeState { get; private set; } = WalkingState;
    public bool DepartureAdmitted => NativeState is not
        (WalkingState or BrakingState or SettledState);
    public int NativeHeroTargetStatus => NativeState switch
    {
        WalkingState or BrakingState => 1,
        SettledState => 2,
        _ => 0,
    };

    /// <summary>
    /// Re-requesting a target updates position/heading/flag but retains an
    /// existing 0x65..0x67 state, as hero_cam_target_set does.
    /// </summary>
    public void Retarget(Rac1TaxiPoint target, double heading, int flag)
    {
        if (!IsFinite(target) || !double.IsFinite(heading))
            throw new ArgumentOutOfRangeException(nameof(target));
        Target = target;
        TargetYaw = Wrap(heading);
        Flag = flag;
        if (DepartureAdmitted)
            NativeState = WalkingState;
    }

    /// <summary>
    /// One observed native player update. The position and speed are owned
    /// by the player controller; the source transitions use planar XY only.
    /// </summary>
    public void Observe(Rac1TaxiPoint player, double yaw, double planarSpeedPerFrame,
        bool animationFinished)
    {
        if (!IsFinite(player) || !double.IsFinite(yaw) ||
            !double.IsFinite(planarSpeedPerFrame) || planarSpeedPerFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(player));
        double dx = player.X - Target.X;
        double dy = player.Z - Target.Z;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        switch (NativeState)
        {
            case WalkingState:
                if (distance >= TargetRadius)
                    return;
                if (planarSpeedPerFrame < StopSpeedPerFrame &&
                    Math.Abs(Wrap(yaw - TargetYaw)) < FacingTolerance)
                    NativeState = SettledState;
                else if (MovementMode != 0 && planarSpeedPerFrame >= StopSpeedPerFrame)
                    NativeState = BrakingState;
                break;
            case BrakingState:
                if (animationFinished)
                    NativeState = WalkingState;
                break;
            case SettledState:
                if (animationFinished || Flag != 0)
                    NativeState = 0;
                if (distance > TargetRadius)
                    NativeState = WalkingState;
                break;
        }
    }

    private static bool IsFinite(Rac1TaxiPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        double.IsFinite(point.Z);

    private static double Wrap(double angle) => Math.IEEERemainder(angle, 2d * Math.PI);
}
