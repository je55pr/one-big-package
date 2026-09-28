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
    private void UpdateHud(bool onFloor)
    {
        var p = GlobalPosition;
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        string diagnostics = DevelopmentControls.DiagnosticsVisible ? BuildInputDiagnostics() : string.Empty;
        string developmentControls = DevelopmentControls.ControlSummary(UseRac1Gameplay);
        _hud.Text =
            $"pos {p.X:0.0} {p.Y:0.0} {p.Z:0.0}    speed {speed:0.0} u/s    {(DevelopmentControls.FlyEnabled ? "FLY" : onFloor ? "ground" : "air")}" +
            $"    anim {AnimationState}\n" +
            $"controller {MovementControllerLabel}    locomotion {_rac1Movement.LocomotionState}    yaw {_rac1Movement.YawMode}\n" +
            $"last jump: {_lastJump}\n" +
            diagnostics +
            $"WASD / left stick / Space + south face jump / C + right shoulder crouch / X + west face action\n" +
            developmentControls;
    }

    private string BuildInputDiagnostics()
    {
        var analogue = _rac1Movement.AnalogueInput;
        string pad = _liveInput.Diagnostic is { } diagnostic ? diagnostic.Format() : "none";
        string inputLine = FormattableString.Invariant(
            $"input host right/forward=({_liveInput.Move.X:0.000000},{-_liveInput.Move.Y:0.000000}) conditioned=({analogue.X:0.000000},{analogue.Y:0.000000}) mag={analogue.Magnitude:0.000000} uncapped={analogue.UncappedMagnitude:0.000000} band={analogue.SpeedBand}\n");
        string surfaceIntent = _rac1SurfaceActionIntent is { } intent
            ? $"0x{intent.NativeActionState:x2}/{intent.Interaction}"
            : "none";
        string movementLine = FormattableString.Invariant(
            $"native target step={_rac1Movement.TargetPlanarStep:0.00000000} actual step={Math.Sqrt((_rac1Movement.PlanarX * _rac1Movement.PlanarX) + (_rac1Movement.PlanarY * _rac1Movement.PlanarY)):0.00000000} locomotion={_rac1Movement.LocomotionState} yaw-mode={_rac1Movement.YawMode} surface-intent={surfaceIntent}\n");
        string yawLine = FormattableString.Invariant(
            $"yaw control={_rac1Yaw.ControlYaw:0.000000} target={_rac1Yaw.TargetYaw:0.000000} current={_rac1Yaw.CurrentYaw:0.000000} velocity={_rac1Yaw.YawVelocity:0.000000}\n");
        string cameraState = _rac1RuntimeCameraState is { } state
            ? FormattableString.Invariant(
                $" control={state.ControlHeadingRadians:0.000000} preferred={state.PreferredDistance:0.000} effective={state.EffectiveDistance:0.000}")
            : string.Empty;
        string cameraLine = FormattableString.Invariant(
            $"camera mode={CameraControllerLabel}{cameraState} manual=({_rac1Camera.ManualYawState:0.000000},{_rac1Camera.ManualPitchState:0.000000}) obstruction={_rac1Camera.ObstructionCorrection:0.000}/{_rac1Camera.ObstructionReleaseTicks} raw=({RawCameraIntent.X:0.000000},{RawCameraIntent.Y:0.000000})\n");
        return inputLine + movementLine + yawLine + cameraLine + $"gamepad {pad}\n";
    }
}
