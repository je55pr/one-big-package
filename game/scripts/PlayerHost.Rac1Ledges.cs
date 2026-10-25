using Godot;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Game;

public partial class PlayerHost
{
    private readonly Rac1LedgeTraversalSession _rac1Ledge = new();
    private double? _rac1TraversalFloorHeight;
    private int _rac1LedgeRegrabDelay;
    private int? _rac1LedgeJumpPressAge;
    private bool _rac1LedgeCrouchWasHeld;
    public double Rac1TraversalAuthoredAnimationTime { get; set; }
    public bool Rac1TraversalAnimationCycleCompleted { get; set; }
    public bool Rac1Hanging => _rac1Ledge.Active;

    private bool TryStepRac1Ledge(Vector2 move, bool jump, bool jumpPressed, bool crouch,
        Rac1PlayerContactResult contact, Rac1MovementContext context,
        out Rac1RatchetMovementController.StepResult step)
    {
        step = default;
        bool dropPressed = crouch && !_rac1LedgeCrouchWasHeld;
        _rac1LedgeCrouchWasHeld = crouch;
        _rac1LedgeRegrabDelay = Math.Max(0, _rac1LedgeRegrabDelay - 1);
        if (jumpPressed) _rac1LedgeJumpPressAge = 0;
        else if (_rac1LedgeJumpPressAge is int age) _rac1LedgeJumpPressAge = Math.Min(age + 1, 1000);
        if (!_rac1Ledge.Active)
        {
            bool eligible = !contact.IsGrounded && _rac1LedgeRegrabDelay == 0 &&
                _rac1TraversalFloorHeight.HasValue &&
                Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(_rac1Movement.CurrentNativeMovementAction,
                    _rac1Movement.CurrentMovementDomain, false, _rac1Movement.VerticalStep,
                    Rac1TraversalAuthoredAnimationTime) &&
                contact.SurfaceInteraction == Rac1SurfaceInteractionKind.None;
            if (!eligible || ProbeRac1Ledge(_rac1TraversalFloorHeight!.Value) is not { } candidate)
                return false;
            double contactPlanar = _rac1Movement.ContactProcessedMotion is { } motion
                ? Math.Sqrt(motion.X * motion.X + motion.Y * motion.Y)
                : new Vector2(Velocity.X, Velocity.Z).Length() / 60d;
            _rac1Ledge.TryGrab(candidate, contactPlanar);
            _rac1Movement.CancelOrdinaryPlanarMotionForAction();
            Rac1TraversalAuthoredAnimationTime = 0;
            _rac1LedgeJumpPressAge = null;
        }
        var position = ScenePlayerToNative(GlobalPosition);
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(move.X, -move.Y);
        double targetYaw = analogue.IsActive
            ? Rac1RatchetYawController.BuildMovementTarget(analogue.X, analogue.Y, context.ControlYaw)
            : _rac1Yaw.CurrentYaw;
        var ledge = _rac1Ledge.Step(new(
            new(position.X, position.Y, position.Z), _rac1Yaw.CurrentYaw, targetYaw,
            analogue.Magnitude, Rac1TraversalAuthoredAnimationTime, Rac1TraversalAnimationCycleCompleted,
            dropPressed, _rac1LedgeJumpPressAge),
            sample => ProbeRac1Ledge(_rac1TraversalFloorHeight!.Value, false, sample));
        if (ledge.ExitKind != Rac1LedgeExitKind.None)
        {
            _rac1LedgeRegrabDelay = 20;
            _rac1Movement.ResumeAfterTraversal(0, 0, 0);
            if (ledge.ExitKind == Rac1LedgeExitKind.LedgeJump)
                _rac1Movement.BeginLedgeJump(context);
            return false;
        }
        // The source supplies the wall-facing target. Snapping its presentation
        // here is an OBP adapter until the ledge yaw recurrence is reconstructed.
        _rac1Yaw.Reset(ledge.FacingTargetYaw);
        UpdateRac1FacingPresentation();
        _rac1PresentationGrounded = false;
        _locomotionAnimationInput = new(false, false, 0,
            InputMagnitude: (float)analogue.Magnitude,
            UncappedInputMagnitude: (float)analogue.UncappedMagnitude,
            Traversal: new(ledge.NativeAction, ledge.Sequence, ledge.SelectionGeneration,
                ledge.EntryFrame, ledge.BlendTicks, Loop: ledge.NativeAction != 0x18));
        step = new(-ledge.Motion.X, ledge.Motion.Y, ledge.Motion.Z,
            Rac1RatchetMovementPhase.Falling, Rac1RatchetLocomotionState.Falling, Rac1RatchetYawMode.Air);
        return true;
    }

    private void ResetRac1LedgeTraversal()
    {
        _rac1Ledge.Reset();
        _rac1TraversalFloorHeight = null;
        _rac1LedgeRegrabDelay = 0;
        _rac1LedgeJumpPressAge = null;
        _rac1LedgeCrouchWasHeld = false;
        Rac1TraversalAuthoredAnimationTime = 0;
        Rac1TraversalAnimationCycleCompleted = false;
    }
}
