using Godot;
using OBP.Core.Math;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    internal double MovementReproControlHeading => GetRac1ControlYaw();
    internal MovementReproPlayer CaptureMovementRepro()
    {
        if (!UseRac1Gameplay || !Rac1GameplayAllowsOrdinaryMovement || DevelopmentControls.FlyEnabled ||
            (!IsOnFloor() && !Rac1RecoveredSupportGrounded) ||
            Rac1MovementPhase != Rac1RatchetMovementPhase.Grounded || Rac1TraversalAction.HasValue ||
            _rac1WrenchAttack.Active || _liveInput.JumpHeld || _liveInput.CrouchHeld ||
            Velocity.Length() > .1f)
            throw new InvalidOperationException("Release movement, turn off fly, and stand still on the ground before saving.");
        EnsureRac1CameraInitialized();
        return new(ToReproVector(GlobalPosition), ToReproVector(Rotation), Rac1CurrentYaw,
            ToReproVector(_yaw.Rotation), ToReproVector(_pitch.Rotation), ToReproVector(Camera.Position),
            _cameraDistance, DevelopmentControls.ForceHostCamera, _rac1CameraInitialized,
            _rac1Camera.CaptureSnapshot(), _rac1RuntimeCameraState, Rac1MovementEquipment,
            Rac1ActiveBackpack, Rac1ClankAvailable, Rac1DevelopmentPackPreview, Rac1DevelopmentSwingshotPreview);
    }

    internal void RestoreMovementRepro(MovementReproPlayer state)
    {
        state.Validate();
        if (DevelopmentControls.FlyEnabled)
            DevelopmentControls.SetFlyEnabled(false);
        ResetAfterPlacement(state.FacingYaw);
        GlobalPosition = FromReproVector(state.Position);
        Rotation = FromReproVector(state.Rotation);
        // Repro coordinates must survive exactly. Spawn snapping can select a
        // different overlapping rock or ceiling and hide the original snag.
        _placed = true;
        _placeTries = 0;
        // Populate CharacterBody's floor cache without retaining any placement
        // correction. The exact saved origin is restored before the next input.
        Velocity = Vector3.Down * (float)((Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick + 2 * SafeMargin) * 60);
        MoveAndSlide();
        GlobalPosition = FromReproVector(state.Position);
        Velocity = Vector3.Zero;
        _ordinaryContactRequest = null;
        _rac1SmallStepEligible = false;
        _locomotionAnimationInput = default;
        _rac1PresentationGrounded = true;
        Rac1MovementEquipment = state.Equipment;
        Rac1ActiveBackpack = state.ActiveBackpack;
        Rac1ClankAvailable = state.ClankAvailable;
        Rac1DevelopmentPackPreview = state.PackPreview;
        Rac1DevelopmentSwingshotPreview = state.SwingshotPreview;
        _yaw.Rotation = FromReproVector(state.HostCameraYawRotation);
        _pitch.Rotation = FromReproVector(state.HostCameraPitchRotation);
        _cameraDistance = (float)state.CameraDistance;
        _rac1Camera.RestoreSnapshot(state.CameraController);
        _rac1CameraInitialized = state.CameraInitialized;
        _rac1RuntimeCameraState = state.Camera;
        DevelopmentControls.SetForceHostCamera(state.ForceHostCamera);
        RefreshDevelopmentCameraPresentation();
        if (!HasActiveRecoveredCamera)
            Camera.Position = FromReproVector(state.HostCameraLocalPosition);
        UpdateRac1FacingPresentation();
    }

    private static Vec3 ToReproVector(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 FromReproVector(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
