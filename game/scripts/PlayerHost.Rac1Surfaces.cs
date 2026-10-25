using Godot;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class PlayerHost
{
    private readonly Rac1SurfaceMovementSession _rac1SurfaceMotion = new();
    private Rac1NativeVector3? _rac1SurfaceRequest;
    private Vector3 _rac1SurfaceStart;
    private bool _rac1IceActive;
    public bool Rac1IceActive => _rac1IceActive;

    private bool TryStepRac1Surface(Vector2 move, bool jump, bool jumpPressed, bool crouch,
        ref Rac1PlayerContactResult contact, Vector3? groundNormal, Rac1MovementContext context)
    {
        _rac1SurfaceRequest = null;
        if (_rac1Movement.Phase != Rac1RatchetMovementPhase.Grounded && !_rac1Movement.IsOrdinaryEdgeFall)
        {
            // Once a jump owns the action, its vertical controller must leave
            // the surface. The preceding floor ray can still see that material.
            contact = contact with { Face = null };
            _rac1IceActive = false;
            return false;
        }
        if (contact.SurfaceInteraction != Rac1SurfaceInteractionKind.IceSlide)
        {
            if (_rac1IceActive)
            {
                _rac1Movement.ResumeAfterTraversal(Velocity.X / 60d, Velocity.Z / 60d, Velocity.Y / 60d);
                _rac1IceActive = false;
            }
            if (contact.SurfaceInteraction == Rac1SurfaceInteractionKind.None)
                return false;
            // Unrecovered water volumes and curved Magneboot support cannot be
            // executed by the ordinary controller. Stop at the boundary instead
            // of throwing each physics update or inventing normal-floor motion.
            Velocity = Vector3.Zero;
            return true;
        }
        if (!_rac1IceActive)
        {
            var carry = _rac1Movement.ContactProcessedMotion;
            _rac1SurfaceMotion.Reset(carry is { } c ? new(-c.X, c.Y, c.Z) : default);
            _rac1IceActive = true;
        }
        var point = ScenePlayerToNative(GlobalPosition);
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(move.X, -move.Y);
        double targetYaw = analogue.IsActive
            ? Rac1RatchetYawController.BuildMovementTarget(analogue.X, analogue.Y, context.ControlYaw)
            : _rac1Yaw.CurrentYaw;
        Vector3 normal = groundNormal ?? Vector3.Up;
        var surface = _rac1SurfaceMotion.Step(new(Rac1SurfaceInteractionKind.IceSlide, contact.IsGrounded,
            new(point.X, point.Y, point.Z), point.Z, new(-normal.X, normal.Z, normal.Y),
            targetYaw, analogue.Magnitude, crouch, jumpPressed ? 0 : null));
        if (surface.RequestedJumpAction == 7)
        {
            _rac1Movement.ResumeAfterTraversal(Velocity.X / 60d, Velocity.Z / 60d, 0);
            // Recontact first, then the normal jump admission uses its existing
            // eight-update anticipation; no second ice request is stacked on it.
            _rac1IceActive = false;
            contact = contact with { Face = null };
            return false;
        }
        _rac1Yaw.Step(move.X, -move.Y, context.ControlYaw,
            crouch ? Rac1RatchetYawMode.CrouchTurn : Rac1RatchetYawMode.GroundRun);
        UpdateRac1FacingPresentation();
        _rac1SurfaceRequest = surface.Motion;
        _rac1SurfaceStart = GlobalPosition;
        _rac1PresentationGrounded = contact.IsGrounded;
        _locomotionAnimationInput = new(false, contact.IsGrounded, 0,
            InputMagnitude: (float)analogue.Magnitude,
            Traversal: new(surface.NativeAction, surface.Sequence, surface.SelectionGeneration,
                0, surface.BlendTicks, Loop: true));
        Velocity = LedgeNativeToScene(surface.Motion) * 60f;
        return true;
    }

    private void RecordRac1SurfaceMotion()
    {
        if (_rac1SurfaceRequest is not { } request) return;
        Vector3 delta = GlobalPosition - _rac1SurfaceStart;
        _rac1SurfaceMotion.RecordPrimaryContact(request, new(-delta.X, delta.Z, delta.Y));
    }

    private void ResetRac1SurfaceMotion()
    {
        _rac1SurfaceMotion.Reset();
        _rac1SurfaceRequest = null;
        _rac1IceActive = false;
    }
}
