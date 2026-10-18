using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    private bool _rac1WrenchPressed;
    private bool _rac1WrenchReturnedToGround;
    public Rac1WrenchAttackSession.Frame Rac1WrenchFrame { get; private set; }
    public bool Rac1WrenchThrown { get; set; }
    public bool Rac1WrenchAnimationBlending { get; set; }
    public event Action<Rac1WrenchAttackSession.Frame>? Rac1WrenchPhysicsUpdated;

    private bool TryStepRac1Wrench(bool jumpPressed, bool crouch,
        Rac1PlayerContactResult contact, Vector3? normal, Rac1MovementContext context)
    {
        bool wasActive = _rac1WrenchAttack.Active;
        long generation = _rac1WrenchAttack.Generation;
        var frame = _rac1WrenchAttack.Step(new(
            _rac1WrenchPressed, jumpPressed, crouch, contact.MovementFacts.IsGrounded,
            _rac1Movement.CurrentNativeMovementAction, _rac1Movement.TraversalAge,
            (float)Rac1TraversalAuthoredAnimationTime, _rac1Movement.VerticalStep <= 0,
            context.GroundClearance, (float)_rac1Yaw.CurrentYaw,
            (float)_rac1Movement.PlanarMagnitude, Velocity.Y / 60f,
            (float)Rac1TraversalAuthoredAnimationTime, Rac1WrenchAnimationBlending, Rac1WrenchThrown,
            _rac1Movement.CurrentFlipDirection));
        _rac1WrenchPressed = false;
        Rac1WrenchFrame = frame;
        if (!frame.Active)
        {
            if (wasActive)
            {
                bool groundHandoff = contact.IsGrounded && Velocity.Y <= 0;
                _rac1Movement.ResumeAfterExternalAction(Velocity.X / 60f, Velocity.Z / 60f,
                    Velocity.Y / 60f, groundHandoff);
                _rac1WrenchReturnedToGround = groundHandoff && !frame.JumpCancelled;
            }
            return false;
        }
        if (!wasActive || generation != frame.Generation)
        {
            _ordinaryContactRequest = null;
            _rac1Movement.CancelOrdinaryPlanarMotionForAction();
            _rac1WrenchReturnedToGround = false;
            GD.Print($"[rac1-wrench] action={frame.NativeAction:x} profile={frame.ProfileId} generation={frame.Generation}");
        }
        var planar = GetRac1NativePlanarBasis().Transform(frame.PlanarX, frame.PlanarY);
        bool grounded = contact.MovementFacts.IsGrounded && frame.Vertical <= 0;
        var request = new Rac1RatchetMovementController.StepResult(
            planar.X, planar.Y, frame.Vertical,
            grounded ? Rac1RatchetMovementPhase.Grounded : frame.Vertical > 0
                ? Rac1RatchetMovementPhase.Rising : Rac1RatchetMovementPhase.Falling,
            grounded ? Rac1RatchetLocomotionState.Moving : frame.Vertical > 0
                ? Rac1RatchetLocomotionState.Rising : Rac1RatchetLocomotionState.Falling,
            Rac1RatchetYawMode.Air);
        var preContact = ResolveRac1PreContactStep(request, contact, normal);
        var resolved = contact.ApplySupportAndConveyor(new(preContact.PlanarX, preContact.Vertical, preContact.PlanarY));
        Velocity = new((float)resolved.X * 60f, (float)resolved.Y * 60f, (float)resolved.Z * 60f);
        _rac1PresentationGrounded = grounded;
        _locomotionAnimationInput = new(false, grounded, (float)Math.Sqrt(planar.X * planar.X + planar.Y * planar.Y),
            Traversal: frame.Animation);
        UpdateRac1FacingPresentation();
        return true;
    }

    private void ApplyRac1WrenchGroundAnimationHandoff()
    {
        if (!_rac1WrenchReturnedToGround)
            return;
        _rac1WrenchReturnedToGround = false;
        _locomotionAnimationInput = _locomotionAnimationInput with
        {
            GroundEntry = new(0, 0, _rac1WrenchAttack.Generation, BlendTicks: -2),
        };
    }

    private void ResetRac1WrenchAttack()
    {
        _rac1WrenchAttack.Reset();
        Rac1WrenchFrame = default;
        _rac1WrenchPressed = _rac1WrenchReturnedToGround = Rac1WrenchThrown = false;
    }
}
