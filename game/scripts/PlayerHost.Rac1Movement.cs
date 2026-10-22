using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Godot.Camera;
using OBP.Godot.Controls;
using OBP.Godot.Player;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Camera;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class PlayerHost : CharacterBody3D
{
    public event Action<double, PlayerAnimationState, Rac1LocomotionAnimationInput>? AnimationPhysicsUpdated;
    public event Action? AnimationPhysicsReset;
    private Rac1LocomotionAnimationInput _locomotionAnimationInput;
    private Rac1NativeVector3? _ordinaryContactRequest;
    private Vector3 _ordinaryContactStart;
    private bool _rac1SmallStepEligible;

    private void StepRetailDerivedMovement(Vector2 move, bool jump, bool crouch)
    {
        _locomotionAnimationInput = default;
        _ordinaryContactRequest = null;
        if (_rac1CrankInputActive)
        {
            ResetRac1WrenchAttack();
            Velocity = Vector3.Zero;
            return;
        }
        _rac1SmallStepEligible = false;
        _rac1TetherRequest = null;
        _rac1SurfaceRequest = null;
        if (UseRac1Gameplay && TryStepRac1Swimming(move, jump, crouch))
        {
            _rac1JumpWasHeld = jump;
            return;
        }
        bool grounded = IsOnFloor();
        bool jumpPressed = jump && !_rac1JumpWasHeld;
        _rac1JumpWasHeld = jump;
        double controlYaw = GetRac1ControlYaw();
        var (contact, groundNormal) = ProbeRac1Contact(
            grounded,
            IsOnCeiling());
        if (contact.IsGrounded) Rac1AudioSurfaceEffectMode = contact.Face?.EffectMode ?? 0;
        _rac1SurfaceActionIntent = UseRac1Gameplay
            ? Rac1SurfaceActionRouting.Select(contact)
            : null;
        var movementContext = UseRac1Gameplay ? ProbeRac1MovementContext(controlYaw) : default;
        if (UseRac1Gameplay && !_rac1WrenchAttack.Active && TryStepRac1Swingshot(movementContext))
        {
            ResetRac1WrenchAttack();
            return;
        }
        if (UseRac1Gameplay && !_rac1WrenchAttack.Active &&
            TryStepRac1Ledge(move, jump, jumpPressed, crouch, contact, movementContext, out var ledgeStep))
        {
            ResetRac1WrenchAttack();
            Velocity = new Vector3((float)ledgeStep.PlanarX, (float)ledgeStep.Vertical,
                (float)ledgeStep.PlanarY) * 60f;
            return;
        }
        if (UseRac1Gameplay && !_rac1WrenchAttack.Active &&
            TryStepRac1Surface(move, jump, jumpPressed, crouch, ref contact, groundNormal, movementContext))
        {
            // These actions retain separate admission until their wrench
            // interaction is recovered; never replay a stale Square afterward.
            ResetRac1WrenchAttack();
            return;
        }
        if (UseRac1Gameplay && TryStepRac1Wrench(jumpPressed, crouch,
                contact, groundNormal, movementContext))
            return;
        if (UseRac1Gameplay && Rac1WrenchFrame.JumpCancelled)
            jump = jumpPressed = true;

        var intent = new PlayerControlIntent(
            move.X,
            -move.Y,
            jump,
            jumpPressed,
            crouch,
            GetRac1PlanarBasis(),
            GetRac1NativePlanarBasis());
        Func<Rac1RatchetYawMode, double> resolveFacing =
            mode => _rac1Yaw.Step(move.X, -move.Y, controlYaw, mode).CurrentYaw;
        var step = UseRac1Gameplay
            ? _rac1Movement.StepExtended(intent, contact, movementContext, resolveFacing)
            : _rac1Movement.Step(intent, contact.MovementFacts, resolveFacing);
        _rac1PresentationGrounded = contact.MovementFacts.IsGrounded || _rac1Movement.IsOrdinaryEdgeFall;
        if (_rac1Movement.TraversalFacingTargetYaw is double facingTarget)
            _rac1Yaw.Reset(facingTarget);

        _locomotionAnimationInput = new Rac1LocomotionAnimationInput(
            Active: UseRac1Gameplay && _rac1Movement.AnalogueInput.IsActive &&
                _rac1Movement.LocomotionState == Rac1RatchetLocomotionState.Moving &&
                (_rac1Movement.Phase == Rac1RatchetMovementPhase.Grounded || _rac1Movement.IsOrdinaryEdgeFall),
            HelperAdmitted: contact.MovementFacts.IsGrounded,
            ScalarAtSelection: (float)_rac1Movement.PlanarMagnitude,
            CrouchHeld: UseRac1Gameplay && crouch &&
                _rac1Movement.Phase == Rac1RatchetMovementPhase.Grounded,
            InputMagnitude: (float)_rac1Movement.AnalogueInput.Magnitude,
            NativeYawVelocity: (float)_rac1Yaw.YawVelocity,
            UncappedInputMagnitude: (float)_rac1Movement.AnalogueInput.UncappedMagnitude,
            Traversal: _rac1Movement.TraversalAnimation ?? _rac1Movement.OrdinaryAirAnimation,
            GroundEntry: _rac1Movement.GroundAnimationEntry);
        ApplyRac1WrenchGroundAnimationHandoff();

        UpdateRac1FacingPresentation();

        var preContact = UseRac1Gameplay
            ? ResolveRac1PreContactStep(step, contact, groundNormal)
            : new Rac1OrdinaryGroundContactMotion.PreContactStep(
                step.PlanarX,
                step.PlanarY,
                step.Vertical);
        var resolvedDelta = contact.ApplySupportAndConveyor(
            new Rac1NativeVector3(
                preContact.PlanarX,
                preContact.Vertical,
                preContact.PlanarY));
        // Native derives P+0x110 before its separate external-transport pass.
        // Godot combines those passes, so admit this feedback only for static
        // ordinary contact with no support/conveyor transfer. The prospective
        // support-rise envelope is host placement policy, not native request.
        if (UseRac1Gameplay && contact.PersistentSupportMoby is null &&
            contact.SupportCarry.TransformDelta.Length == 0 && contact.Conveyor.TangentialTransfer.Length == 0)
        {
            Vector3 normal = groundNormal ?? Vector3.Up;
            var request = Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(
                step, contact.MovementFacts, normal.X, normal.Z, normal.Y,
                applyOrdinaryJumpAnticipationRequest: _rac1Movement.TraversalAction is null);
            _ordinaryContactRequest = new Rac1NativeVector3(request.PlanarX, request.PlanarY, request.Vertical);
            _ordinaryContactStart = GlobalPosition;
            _rac1SmallStepEligible = contact.MovementFacts.IsGrounded &&
                step.Phase == Rac1RatchetMovementPhase.Grounded &&
                _rac1Movement.TraversalAction is null &&
                _rac1SurfaceActionIntent is null;
        }
        const float nativeTicksPerSecond = (float)Rac1RatchetMovementController.UpdateHz;
        Velocity = new Vector3(
            (float)resolvedDelta.X * nativeTicksPerSecond,
            (float)resolvedDelta.Y * nativeTicksPerSecond,
            (float)resolvedDelta.Z * nativeTicksPerSecond);
        if (Scripted && step.Vertical > 0d && !_scriptJumped)
        {
            _scriptJumped = true;
            GD.Print($"[PlayerHost] native R&C1 jump from {GlobalPosition}");
        }
    }

    private void RecordRac1OrdinaryContactMotion()
    {
        RecordRac1TetherMotion();
        RecordRac1SurfaceMotion();
        if (_ordinaryContactRequest is { } request)
        {
            Vector3 displacement = GlobalPosition - _ordinaryContactStart;
            _rac1Movement.RecordOrdinaryContactMotion(request,
                new Rac1NativeVector3(displacement.X, displacement.Z, displacement.Y));
        }
        else
            _rac1Movement.ClearOrdinaryContactMotion();
        _locomotionAnimationInput = _locomotionAnimationInput with
        {
            ContactMotionLength = _rac1Movement.ContactProcessedMotion is { } motion
                ? (float)motion.Length : null,
        };
    }

    private Rac1OrdinaryGroundContactMotion.PreContactStep ResolveRac1PreContactStep(
        Rac1RatchetMovementController.StepResult step,
        Rac1PlayerContactResult contact,
        Vector3? groundNormal)
    {
        Vector3 admittedNormal = groundNormal ?? Vector3.Up;
        var preContact = Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(
            step,
            contact.MovementFacts,
            admittedNormal.X,
            admittedNormal.Z,
            admittedNormal.Y,
            applyOrdinaryJumpAnticipationRequest: _rac1Movement.TraversalAction is null);

        if (!contact.MovementFacts.IsGrounded ||
            step.Phase != Rac1RatchetMovementPhase.Grounded ||
            Math.Abs(step.PlanarX) + Math.Abs(step.PlanarY) <= 1e-12d ||
            !TryProbeRac1ProspectiveSupport(
                step.PlanarX,
                step.PlanarY,
                out double supportRise,
                out double supportAngle))
        {
            return preContact;
        }

        return preContact with
        {
            Vertical = Rac1OrdinaryGroundContactMotion.ResolveHostTransitionVertical(
                preContact.Vertical,
                supportRise,
                supportAngle),
        };
    }

    private bool TryProbeRac1ProspectiveSupport(
        double planarX,
        double planarY,
        out double supportRise,
        out double supportAngle)
    {
        supportRise = 0d;
        supportAngle = 0d;
        float envelope = (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportTransitionHostEnvelope;
        Vector3 target = GlobalPosition + new Vector3((float)planarX, 0f, (float)planarY);
        var query = PhysicsRayQueryParameters3D.Create(
            target + Vector3.Up * envelope,
            target + Vector3.Down * envelope);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 ||
            !hit.ContainsKey("collider") ||
            !hit.ContainsKey("position") ||
            !hit.ContainsKey("normal") ||
            !hit.ContainsKey("face_index"))
            return false;

        if (hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D collisionBody)
            return false;

        int faceIndex = (int)hit["face_index"];
        int? materialId = collisionBody.MaterialIdForFace(faceIndex);
        if (materialId is null || materialId < byte.MinValue || materialId > byte.MaxValue)
            return false;
        var candidateContact = Rac1PlayerContactResult.StaticWorld(
            true,
            rawFaceType: materialId.Value);
        if (Rac1SurfaceActionRouting.Select(candidateContact) is not null)
            return false;

        Vector3 normal = ((Vector3)hit["normal"]).Normalized();
        supportAngle = Math.Acos(Math.Clamp((double)normal.Dot(Vector3.Up), -1d, 1d));
        supportRise = ((Vector3)hit["position"]).Y - GlobalPosition.Y;
        return true;
    }

    private (Rac1PlayerContactResult Contact, Vector3? GroundNormal) ProbeRac1Contact(
        bool grounded,
        bool hitCeiling)
    {
        Vector3 origin = GlobalPosition;
        float supportMetric = (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportContactMetricLimit;
        float probeUp = grounded ? 0.25f : supportMetric;
        float probeDown = grounded ? 2.0f : supportMetric;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * probeUp,
            origin + Vector3.Down * probeDown);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || !hit.ContainsKey("collider"))
        {
            ClearRac1HostSupportAnchor();
            return (_rac1DynamicSupport.StepStatic(grounded, hitCeiling), null);
        }

        Vector3? groundNormal = hit.ContainsKey("normal")
            ? (Vector3)hit["normal"]
            : null;
        if (!grounded)
        {
            if (groundNormal is not { } candidateNormal ||
                !hit.ContainsKey("position"))
            {
                ClearRac1HostSupportAnchor();
                return (_rac1DynamicSupport.StepStatic(false, hitCeiling), null);
            }

            Vector3 candidatePosition = (Vector3)hit["position"];
            double correctionMetric = Math.Abs(origin.Y - candidatePosition.Y);
            double angle = Math.Acos(Math.Clamp((double)candidateNormal.Dot(Vector3.Up), -1d, 1d));
            if (!Rac1OrdinaryGroundContactMotion.AdmitsOrdinarySupport(correctionMetric, angle))
            {
                ClearRac1HostSupportAnchor();
                return (_rac1DynamicSupport.StepStatic(false, hitCeiling), null);
            }

            grounded = true;
        }
        var collider = hit["collider"].As<Node>();
        if (collider is RuntimeWorldScene.RuntimeCollisionBody3D collisionBody &&
            hit.ContainsKey("face_index"))
        {
            ClearRac1HostSupportAnchor();
            int faceIndex = (int)hit["face_index"];
            int? materialId = collisionBody.MaterialIdForFace(faceIndex);
            int? rawFaceType = materialId is >= byte.MinValue and <= byte.MaxValue
                ? materialId
                : null;
            return (
                _rac1DynamicSupport.StepStatic(
                    true,
                    hitCeiling,
                    rawFaceType),
                groundNormal);
        }

        if (UseRac1Gameplay &&
            RuntimeWorldScene.FindDynamicObjectRoot(collider) is
            { Source: { } dynamicOwner } dynamicRoot)
        {
            var contactKey = new Rac1MobyRuntimeKey(
                dynamicOwner.NativeClassId,
                dynamicOwner.InstanceIndex);
            if (!hit.ContainsKey("position"))
            {
                ClearRac1HostSupportAnchor();
                return (
                    _rac1DynamicSupport.Step(new Rac1DynamicSupportFacts(
                        IsGrounded: true,
                        HitCeiling: hitCeiling,
                        RawFaceType: null,
                        ContactedMoby: contactKey,
                        CurrentDynamicContact: contactKey,
                        PersistentSupportMoby: null,
                        SupportAnchor: new Rac1SupportAnchorState(0u, false),
                        SupportAnchorWorldPosition: null,
                        Conveyor: Rac1ConveyorTransfer.None)),
                    groundNormal);
            }

            Vector3 contactWorld = (Vector3)hit["position"];
            if (!_rac1HostSupportAnchorValid || _rac1HostSupportKey != contactKey)
            {
                _rac1HostSupportKey = contactKey;
                _rac1HostSupportLocalAnchor = dynamicRoot.ToLocal(contactWorld);
                _rac1HostSupportAnchorValid = true;
            }

            Vector3 supportAnchorWorld = dynamicRoot.ToGlobal(_rac1HostSupportLocalAnchor);
            Vec3 supportAnchorNative = ScenePlayerToNative(supportAnchorWorld);
            return (
                _rac1DynamicSupport.Step(new Rac1DynamicSupportFacts(
                    IsGrounded: true,
                    HitCeiling: hitCeiling,
                    RawFaceType: null,
                    ContactedMoby: contactKey,
                    CurrentDynamicContact: contactKey,
                    PersistentSupportMoby: contactKey,
                    SupportAnchor: new Rac1SupportAnchorState(1u, true),
                    SupportAnchorWorldPosition: new Rac1NativeVector3(
                        supportAnchorNative.X,
                        supportAnchorNative.Y,
                        supportAnchorNative.Z),
                    Conveyor: Rac1ConveyorTransfer.None)),
                groundNormal);
        }

        ClearRac1HostSupportAnchor();
        return (_rac1DynamicSupport.StepStatic(true, hitCeiling), groundNormal);
    }

    private void ClearRac1HostSupportAnchor()
    {
        _rac1HostSupportKey = null;
        _rac1HostSupportLocalAnchor = Vector3.Zero;
        _rac1HostSupportAnchorValid = false;
    }

}
