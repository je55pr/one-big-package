namespace OBP.RAC1.Player;

public enum Rac1LedgeExitKind
{
    None,
    Drop,
    LedgeJump,
}

public readonly record struct Rac1LedgeStepInput(
    Rac1NativeVector3 Position,
    double FacingYaw,
    double DesiredYaw,
    double InputMagnitude,
    double AuthoredAnimationTime,
    bool AnimationCycleCompleted,
    bool DropPressed,
    int? JumpPressAgeTicks,
    bool ContactRetained = true);

public readonly record struct Rac1LedgeStepResult(
    Rac1NativeVector3 Motion,
    double FacingTargetYaw,
    int NativeAction,
    int Sequence,
    int EntryFrame,
    int BlendTicks,
    int SelectionGeneration,
    Rac1LedgeExitKind ExitKind);

/// <summary>
/// Source-owned ledge grab/hang/shimmy selection and motion. Query execution,
/// yaw integration and animation sampling are supplied by the application. The
/// exit into action 0x1c is the recovered ledge jump, not a teleport-to-top rule.
/// </summary>
public sealed class Rac1LedgeTraversalSession
{
    public const int GrabAction = 0x18;
    public const int HangAction = 0x19;
    public const int ShimmyPositiveAction = 0x1a;
    public const int ShimmyNegativeAction = 0x1b;
    public const int LedgeJumpAction = 0x1c;
    public const double GrabAnimationExitTime = 13.5f;
    public const double HoldStepLimit = 4f * (1f / 60f);
    public const double ShimmyProbeDistance = 0.3f;
    public const double InputThreshold = 0.5f;
    public const double LateralHeadingTolerance = 0.7853981852531433f;
    public const double MaximumShimmyCornerAngle = 0.5235987901687622f;
    public const double AwayHeadingThreshold = 2.356194496154785f;
    // Preserve the original integration's ledge regrab and jump-buffer tick bounds.
    // These are traversal admission rules, not Godot contact-query state.
    public const int ExitRegrabDelayTicks = 20;
    public const int MaximumJumpPressAgeTicks = 1000;

    private static readonly float[] ShimmySteps =
    [
        -0f, -0f, .025f, .050f, .075f, .162f, .162f, .107f,
        .037f, .037f, .037f, -.017f, -.017f, -.035f, -.035f,
    ];

    private Rac1LedgeCandidate _candidate;
    private int _entryFrame;
    private int _blendTicks;
    public int GrabCooldownTicks { get; private set; }
    public int? JumpPressAgeTicks { get; private set; }
    public bool CanProbeForGrab => !Active && GrabCooldownTicks == 0;
    public bool Active { get; private set; }

    /// <summary>Advance admission once for each host ledge tick, including inactive ticks.</summary>
    public void AdvanceInput(bool jumpPressed)
    {
        GrabCooldownTicks = Math.Max(0, GrabCooldownTicks - 1);
        if (jumpPressed)
            JumpPressAgeTicks = 0;
        else if (JumpPressAgeTicks is int age)
            JumpPressAgeTicks = Math.Min(age + 1, MaximumJumpPressAgeTicks);
    }
    public int NativeAction { get; private set; }
    public int SelectionGeneration { get; private set; }
    public Rac1LedgeCandidate Candidate => _candidate;

    public bool TryGrab(Rac1LedgeCandidate candidate, double contactPlanarLength = 0d)
    {
        if (!double.IsFinite(contactPlanarLength) || contactPlanarLength < 0d)
            throw new ArgumentOutOfRangeException(nameof(contactPlanarLength));
        if (!CanProbeForGrab)
            return false;
        _candidate = candidate;
        JumpPressAgeTicks = null;
        Active = true;
        Select(GrabAction, contactPlanarLength > 3.5f * (1f / 60f) ? 0 : 2, 9);
        return true;
    }

    public void Reset()
    {
        Active = false;
        NativeAction = 0;
        _candidate = default;
        _entryFrame = 0;
        _blendTicks = 0;
        GrabCooldownTicks = 0;
        JumpPressAgeTicks = null;
    }

    public Rac1LedgeStepResult Step(
        in Rac1LedgeStepInput input,
        Func<Rac1NativeVector3, Rac1LedgeCandidate?> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (!Active)
            throw new InvalidOperationException("No ledge traversal is active.");
        if (!double.IsFinite(input.Position.Length) ||
            !double.IsFinite(input.FacingYaw) ||
            !double.IsFinite(input.DesiredYaw) ||
            !double.IsFinite(input.InputMagnitude) || input.InputMagnitude is < 0d or > 1.01d ||
            !double.IsFinite(input.AuthoredAnimationTime) || input.AuthoredAnimationTime < 0d ||
            input.JumpPressAgeTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(input));

        Rac1NativeVector3 motion = NativeAction is ShimmyPositiveAction or ShimmyNegativeAction
            ? ResolveShimmyMotion(input, probe)
            : ResolveHoldMotion(input.Position, _candidate.HangPosition);

        if (NativeAction == GrabAction)
        {
            if (input.AuthoredAnimationTime > GrabAnimationExitTime)
                Select(HangAction, 0, 6);
            return Result(motion);
        }

        if (!input.ContactRetained || input.DropPressed ||
            input.InputMagnitude > InputThreshold && input.JumpPressAgeTicks is >= 0 and < 9 &&
            YawDistance(input.DesiredYaw, input.FacingYaw) > AwayHeadingThreshold)
            return Exit(Rac1LedgeExitKind.Drop);

        if (input.JumpPressAgeTicks is >= 0 and < 17)
            return Exit(Rac1LedgeExitKind.LedgeJump);

        if (NativeAction == HangAction)
        {
            int direction = SideDirection(input.DesiredYaw, input.FacingYaw, input.InputMagnitude);
            if (direction != 0 && probe(SidePosition(input.Position, input.FacingYaw, direction)) is not null)
                Select(direction > 0 ? ShimmyPositiveAction : ShimmyNegativeAction, 0, 6);
        }
        else
        {
            int direction = NativeAction == ShimmyPositiveAction ? 1 : -1;
            bool stillMoving = input.InputMagnitude >= InputThreshold &&
                YawDistance(input.DesiredYaw, input.FacingYaw + direction * Math.PI / 2d) <= LateralHeadingTolerance &&
                probe(SidePosition(input.Position, input.FacingYaw, direction)) is not null;
            if (!stillMoving && input.AnimationCycleCompleted)
                Select(HangAction, 0, 6);
        }

        return Result(motion);
    }

    /// <summary>The source uses a signed authored-time lookup, not constant shimmy velocity.</summary>
    public static double ShimmyStepAt(double authoredAnimationTime)
    {
        if (!double.IsFinite(authoredAnimationTime) || authoredAnimationTime < 0d || authoredAnimationTime >= ShimmySteps.Length)
            throw new ArgumentOutOfRangeException(nameof(authoredAnimationTime));
        return ShimmySteps[(int)authoredAnimationTime] * 0.5f;
    }

    public static Rac1NativeVector3 ResolveHoldMotion(
        Rac1NativeVector3 position, Rac1NativeVector3 target)
    {
        var delta = new Rac1NativeVector3(target.X - position.X, target.Y - position.Y, target.Z - position.Z);
        double length = delta.Length;
        if (length <= HoldStepLimit)
            return delta;
        double scale = HoldStepLimit / length;
        return new(delta.X * scale, delta.Y * scale, delta.Z * scale);
    }

    private Rac1NativeVector3 ResolveShimmyMotion(
        in Rac1LedgeStepInput input,
        Func<Rac1NativeVector3, Rac1LedgeCandidate?> probe)
    {
        int direction = NativeAction == ShimmyPositiveAction ? 1 : -1;
        var first = probe(input.Position);
        var second = probe(SidePosition(input.Position, input.FacingYaw, direction));
        if (first is not { } a || second is not { } b ||
            YawDistance(a.FacingYaw, b.FacingYaw) > MaximumShimmyCornerAngle)
            return Rac1NativeVector3.Zero;

        double yaw = Rac1LedgeProbe.WrapYaw(a.WallNormalYaw +
            Rac1LedgeProbe.WrapYaw(b.WallNormalYaw - a.WallNormalYaw) * .5d);
        var target = new Rac1NativeVector3(
            (a.HangPosition.X + b.HangPosition.X) * .5d,
            (a.HangPosition.Y + b.HangPosition.Y) * .5d,
            (a.HangPosition.Z + b.HangPosition.Z) * .5d);
        _candidate = a with { HangPosition = target, WallNormalYaw = yaw };
        double x = target.X - input.Position.X;
        double y = target.Y - input.Position.Y;
        double planarLength = Math.Sqrt(x * x + y * y);
        double step = ShimmyStepAt(input.AuthoredAnimationTime);
        double scale = planarLength > 1e-12d ? step / planarLength : 0d;
        return new(x * scale, y * scale, target.Z - input.Position.Z);
    }

    private Rac1LedgeStepResult Result(Rac1NativeVector3 motion) => new(
        motion, _candidate.FacingYaw, NativeAction, Sequence(NativeAction),
        _entryFrame, _blendTicks, SelectionGeneration, Rac1LedgeExitKind.None);

    private Rac1LedgeStepResult Exit(Rac1LedgeExitKind exit)
    {
        Active = false;
        GrabCooldownTicks = ExitRegrabDelayTicks;
        int action = exit == Rac1LedgeExitKind.LedgeJump ? LedgeJumpAction : 6;
        return new(Rac1NativeVector3.Zero, _candidate.FacingYaw, action,
            exit == Rac1LedgeExitKind.LedgeJump ? 34 : 10, 0,
            exit == Rac1LedgeExitKind.LedgeJump ? 4 : 10, ++SelectionGeneration, exit);
    }

    private void Select(int action, int entryFrame, int blendTicks)
    {
        NativeAction = action;
        _entryFrame = entryFrame;
        _blendTicks = blendTicks;
        SelectionGeneration++;
    }

    private static int Sequence(int action) => action switch
    {
        GrabAction => 32,
        HangAction => 33,
        ShimmyPositiveAction => 36,
        ShimmyNegativeAction => 37,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static int SideDirection(double desiredYaw, double facingYaw, double magnitude)
    {
        if (magnitude <= InputThreshold)
            return 0;
        if (YawDistance(desiredYaw, facingYaw + Math.PI / 2d) < LateralHeadingTolerance)
            return 1;
        return YawDistance(desiredYaw, facingYaw - Math.PI / 2d) < LateralHeadingTolerance ? -1 : 0;
    }

    private static Rac1NativeVector3 SidePosition(Rac1NativeVector3 position, double facingYaw, int direction) =>
        new(position.X + Math.Cos(facingYaw + direction * Math.PI / 2d) * ShimmyProbeDistance,
            position.Y + Math.Sin(facingYaw + direction * Math.PI / 2d) * ShimmyProbeDistance,
            position.Z);

    private static double YawDistance(double first, double second) =>
        Math.Abs(Rac1LedgeProbe.WrapYaw(first - second));
}
