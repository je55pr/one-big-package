namespace OBP.Runtime.Player;

/// <summary>
/// Movement facts supplied by the authoritative player controller for one
/// deterministic animation-state update.
/// </summary>
public readonly record struct PlayerAnimationFacts(
    bool IsGrounded,
    double PlanarSpeed,
    double VerticalVelocity,
    bool JustLanded = false,
    bool AttackRequested = false);

/// <summary>
/// Converts authoritative movement facts into semantic presentation states.
/// The machine never changes position, velocity, grounded state, or inputs.
/// </summary>
public sealed class PlayerAnimationStateMachine
{
    public const double WalkEnterSpeed = 0.20;
    public const double WalkExitSpeed = 0.10;
    public const double RunEnterSpeed = 4.00;
    public const double RunExitSpeed = 3.50;
    public const double JumpRiseVelocity = 0.05;

    private PlayerAnimationState _movementState = PlayerAnimationState.Idle;
    private bool _landingConsumedWhileGrounded;

    public PlayerAnimationState State { get; private set; } = PlayerAnimationState.Idle;

    public PlayerAnimationState Update(PlayerAnimationFacts facts)
    {
        _movementState = ResolveMovement(facts);
        State = facts.AttackRequested ? PlayerAnimationState.Attack : _movementState;
        return State;
    }

    private PlayerAnimationState ResolveMovement(PlayerAnimationFacts facts)
    {
        if (!facts.IsGrounded)
        {
            _landingConsumedWhileGrounded = false;
            return facts.VerticalVelocity > JumpRiseVelocity
                ? PlayerAnimationState.JumpRise
                : PlayerAnimationState.Fall;
        }

        if (facts.JustLanded && !_landingConsumedWhileGrounded)
        {
            _landingConsumedWhileGrounded = true;
            return PlayerAnimationState.Land;
        }

        double speed = System.Math.Max(0, facts.PlanarSpeed);
        return ResolveGroundedLocomotion(speed);
    }

    private PlayerAnimationState ResolveGroundedLocomotion(double speed)
    {
        return _movementState switch
        {
            PlayerAnimationState.Run => ResolveFromRun(speed),
            PlayerAnimationState.Walk => ResolveFromWalk(speed),
            _ => ResolveFromNeutral(speed),
        };
    }

    private static PlayerAnimationState ResolveFromRun(double speed)
    {
        if (speed <= WalkExitSpeed)
            return PlayerAnimationState.Idle;

        return speed < RunExitSpeed
            ? PlayerAnimationState.Walk
            : PlayerAnimationState.Run;
    }

    private static PlayerAnimationState ResolveFromWalk(double speed)
    {
        if (speed <= WalkExitSpeed)
            return PlayerAnimationState.Idle;

        return speed >= RunEnterSpeed
            ? PlayerAnimationState.Run
            : PlayerAnimationState.Walk;
    }

    private static PlayerAnimationState ResolveFromNeutral(double speed)
    {
        if (speed >= RunEnterSpeed)
            return PlayerAnimationState.Run;

        return speed >= WalkEnterSpeed
            ? PlayerAnimationState.Walk
            : PlayerAnimationState.Idle;
    }
}
