namespace OBP.RAC1.Player;

/// <summary>
/// Action-0x2c attached class-803 swing, bounded to static targets and ordinary
/// world gravity. Native entry 0x223bbc, motion 0x219e70..0x21a720. The caller
/// owns the Swingshot projectile's attachment transition and source facing.
/// </summary>
public sealed class Rac1TraversalEquipmentSwing
{
    public const int Action = 0x2c;
    public const int EntrySequence = 52;
    public const int EntryBlendTicks = 11;
    public const float EntryAnimationMultiplier = 0.7f;
    public const int LoopSequence = 53;
    public const int LoopNextFrame = 10;
    public const int LoopBlendTicks = 12;
    public const float LoopAnimationMultiplier = 0.3f;
    public const double SecondsPerUpdate = 1d / 60d;
    public const double GravityStep = 40d / 3600d;
    public const double InitialGravityStep = 57d / 3600d;

    private Rac1TraversalEquipmentSwingTarget _target;
    private Rac1NativeVector3 _request;
    private double _radiusVelocity;
    private double _initialForwardVelocity;

    public bool Active { get; private set; }
    public int AgeTicks { get; private set; }
    public double Radius { get; private set; }
    public double EntryFacingYaw { get; private set; }
    public bool InitialBoost { get; private set; }

    public void Attach(Rac1TraversalEquipmentSwingTarget target,
        Rac1NativeVector3 position, Rac1NativeVector3 anchor, Rac1NativeVector3 initialRequest)
    {
        Validate(position);
        Validate(anchor);
        Validate(initialRequest);
        if (target.FollowsTargetFacing)
            throw new NotSupportedException("Moving/facing-driven R&C1 swing targets require their live target owner.");
        if (!float.IsFinite(target.Radius) || target.Radius <= 0 ||
            !float.IsFinite(target.MaximumRequestLength) || target.MaximumRequestLength < 0)
            throw new ArgumentOutOfRangeException(nameof(target));
        _target = target;
        _request = initialRequest;
        _radiusVelocity = 0;
        _initialForwardVelocity = 0;
        Radius = Difference(anchor, position).Length;
        EntryFacingYaw = Math.Atan2(anchor.Y - position.Y, anchor.X - position.X);
        AgeTicks = 0;
        InitialBoost = true;
        Active = true;
    }

    /// <summary>
    /// Request and direct native position correction are separate source outputs.
    /// Last translation Z is P+0x100.Z, while Pe0 retains its planar request.
    /// Input yaw/magnitude are already conditioned source controls.
    /// </summary>
    public Rac1TraversalEquipmentSwingStep Step(Rac1NativeVector3 position,
        Rac1NativeVector3 anchor, Rac1NativeVector3 lastTranslation,
        double nativeFacingYaw, double inputYaw, double inputMagnitude,
        double nativeSupportDistance, bool unsupported,
        bool nativePresentationTransition = false)
    {
        if (!Active)
            throw new InvalidOperationException("Attach the native swing session before stepping it.");
        Validate(position);
        Validate(anchor);
        Validate(lastTranslation);
        if (!double.IsFinite(nativeFacingYaw) || !double.IsFinite(inputYaw))
            throw new ArgumentOutOfRangeException(nameof(nativeFacingYaw));
        if (!double.IsFinite(inputMagnitude) || inputMagnitude is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(inputMagnitude));
        if (!double.IsFinite(nativeSupportDistance) || nativeSupportDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeSupportDistance));

        double previousVertical = _request.Z;
        bool below = position.Z < anchor.Z;
        var request = _request;
        if (below && AgeTicks > 40 && inputMagnitude > 0.25)
        {
            double pump = Math.Min(0.21f * SecondsPerUpdate * inputMagnitude, 5 * SecondsPerUpdate);
            request += new Rac1NativeVector3(Math.Cos(inputYaw) * pump, Math.Sin(inputYaw) * pump, 0);
        }
        double planarDistance = Math.Sqrt(
            (position.X - anchor.X) * (position.X - anchor.X) +
            (position.Y - anchor.Y) * (position.Y - anchor.Y));
        if (planarDistance < _target.Radius * (double)1.57f || nativeSupportDistance > 1.5)
        {
            request = new(request.X, request.Y, lastTranslation.Z - GravityStep);
            if (InitialBoost && request.Z > 0)
                request = new(request.X, request.Y, request.Z - InitialGravityStep);
        }
        request = Scale(request, 1 - 0.015f * SecondsPerUpdate);

        double length = request.Length;
        if (length > _target.MaximumRequestLength)
        {
            double nextLength = Math.Max(_target.MaximumRequestLength, length - 15d / 3600d);
            request = SetLength(request, nextLength);
        }

        var attachment = new Rac1NativeVector3(
            position.X + Math.Sin(nativeFacingYaw) * 0.15f,
            position.Y - Math.Cos(nativeFacingYaw) * 0.15f,
            position.Z);
        var predicted = attachment + request;
        var radial = SetLength(Difference(predicted, anchor), Radius);
        var constrained = anchor + radial;
        request = Difference(constrained, attachment);

        double oldRadius = Radius;
        Radius = AdvanceDampedSeek(Radius, ref _radiusVelocity, _target.Radius,
            _target.RadiusGain, _target.RadiusDamping, _target.RadiusMaximumStep);
        var positionCorrection = Radius < radial.Length
            ? SetLength(radial, Radius - oldRadius)
            : Rac1NativeVector3.Zero;

        if (InitialBoost && below)
        {
            double planar = Math.Sqrt(request.X * request.X + request.Y * request.Y);
            if (planar < 14d * SecondsPerUpdate)
            {
                _ = AdvanceDampedSeek(planar, ref _initialForwardVelocity,
                    14d * SecondsPerUpdate, 0.047f, 0.3f, 0);
                request += new Rac1NativeVector3(
                    Math.Cos(EntryFacingYaw) * _initialForwardVelocity,
                    Math.Sin(EntryFacingYaw) * _initialForwardVelocity, 0);
            }
        }
        if (unsupported && ((previousVertical < 0 && request.Z > 0) || nativePresentationTransition))
            InitialBoost = false;
        _request = request;
        AgeTicks++;
        return new(request, positionCorrection, Radius);
    }

    public Rac1TraversalEquipmentSwingExit? EvaluateExit(bool useHeld)
    {
        if (!Active || useHeld || AgeTicks <= 10)
            return null;
        Active = false;
        return new(0x2d, AgeTicks < 40, 11, 15, 27d / 3600d, 7d / 3600d);
    }

    private static double AdvanceDampedSeek(double value, ref double velocity,
        double target, double gain, double damping, double maximumStep)
    {
        double error = target - value;
        velocity += gain * error - damping * velocity;
        if (maximumStep > 0)
            velocity = Math.Clamp(velocity, -maximumStep, maximumStep);
        velocity = Math.Clamp(velocity, -Math.Abs(error), Math.Abs(error));
        value += velocity;
        if (Math.Abs(target - value) < maximumStep * 0.01f)
        {
            value = target;
            velocity = 0;
        }
        return value;
    }

    private static Rac1NativeVector3 Difference(Rac1NativeVector3 left, Rac1NativeVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    private static Rac1NativeVector3 Scale(Rac1NativeVector3 vector, double scale) =>
        new(vector.X * scale, vector.Y * scale, vector.Z * scale);
    private static Rac1NativeVector3 SetLength(Rac1NativeVector3 vector, double length) =>
        vector.Length == 0 ? Rac1NativeVector3.Zero : Scale(vector, length / vector.Length);
    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) ||
            !double.IsFinite(vector.Z) || !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

public readonly record struct Rac1TraversalEquipmentSwingStep(
    Rac1NativeVector3 Request,
    Rac1NativeVector3 PositionCorrection,
    double Radius);

public readonly record struct Rac1TraversalEquipmentSwingExit(
    int NativeAction,
    bool PrematureRelease,
    int Sequence,
    int BlendTicks,
    double NativeFallField940,
    double NativeFallField944);
