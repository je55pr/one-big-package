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
    private void EnsureRac1CameraInitialized()
    {
        if (_rac1CameraInitialized)
            return;

        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        double initialHeading = GetRac1ControlYaw();
        _rac1Camera.Reset(nativePlayer, initialHeading);
        var state = _rac1Camera.Step(new Rac1OrdinaryCameraController.Input(
            nativePlayer,
            0d,
            0d,
            default));
        _rac1CameraInitialized = true;
        ApplyRac1CameraState(state);
    }

    private void StepRac1Camera(Vector2 rawCameraIntent)
    {
        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        RuntimeCameraObstructionFacts obstruction = ProbeRac1CameraObstruction();
        var state = _rac1Camera.Step(new Rac1OrdinaryCameraController.Input(
            nativePlayer,
            rawCameraIntent.X,
            -rawCameraIntent.Y,
            obstruction));
        ApplyRac1CameraState(state);
    }

    private RuntimeCameraObstructionFacts ProbeRac1CameraObstruction()
    {
        // The exact retail contact primitive is still intentionally unpromoted.
        // Godot supplies only contact/orientation facts; RAC1 owns the response law.
        double heading = _rac1Camera.ControlHeading;
        Vector3 from = GlobalPosition + Vector3.Up * (float)Rac1OrdinaryCameraController.OrdinaryLookHeight;
        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        var desiredEyeNative = new Vec3(
            nativePlayer.X - (Math.Cos(heading) * Rac1OrdinaryCameraController.OrdinaryPreferredRadius),
            nativePlayer.Y - (Math.Sin(heading) * Rac1OrdinaryCameraController.OrdinaryPreferredRadius),
            nativePlayer.Z + Rac1OrdinaryCameraController.OrdinaryEyeHeight);
        Vector3 to = RuntimeWorldScene.ToScene(
            desiredEyeNative.X,
            desiredEyeNative.Z,
            desiredEyeNative.Y);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return default;

        Vector3 normal = (Vector3)hit["normal"];
        Vector3 sceneRight = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            Math.Sin(heading),
            -Math.Cos(heading));
        return new RuntimeCameraObstructionFacts(
            true,
            normal.Dot(sceneRight));
    }

    private static Vec3 ScenePlayerToNative(Vector3 scene) =>
        new(-scene.X, scene.Z, scene.Y);

    /// <summary>
    /// Supplies a recovered R&amp;C1 camera snapshot. When ordinary R&amp;C1 play
    /// owns the camera, this state drives both the visible pose and movement's
    /// control-relative heading/basis.
    /// </summary>
    public void ApplyRac1CameraState(Rac1CameraState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _rac1RuntimeCameraState = state.ToRuntimeState();
        if (HasActiveRecoveredCamera)
            ApplyRecoveredCameraPresentation();
    }

    /// <summary>Drop recovered camera ownership and return to the debug host camera.</summary>
    public void ClearRac1CameraState()
    {
        _rac1RuntimeCameraState = null;
        RestoreHostCameraPresentation();
    }

    private void ApplyRecoveredCameraPresentation()
    {
        if (_rac1RuntimeCameraState is null)
            return;
        Camera.TopLevel = true;
        RuntimeCameraSceneAdapter.Apply(Camera, _rac1RuntimeCameraState);
    }

    private void RestoreHostCameraPresentation()
    {
        Camera.TopLevel = false;
        Camera.Position = new Vector3(0f, 0f, _cameraDistance);
        Camera.Rotation = Vector3.Zero;
    }

    private double GetRac1ControlYaw()
    {
        if (HasActiveRecoveredCamera)
            return _rac1RuntimeCameraState!.ControlHeadingRadians;

        // Development fallback only. R&C1 owns how raw planar stick direction
        // combines with the supplied forward/control heading to form G+0x100.
        Vector3 sceneForward = _yaw.GlobalTransform.Basis * new Vector3(0f, 0f, -1f);
        sceneForward.Y = 0f;
        if (sceneForward.LengthSquared() <= 1e-8f) return _rac1Yaw.ControlYaw;
        sceneForward = sceneForward.Normalized();
        return Math.Atan2(sceneForward.Z, -sceneForward.X);
    }

    private PlayerPlanarBasis GetRac1PlanarBasis()
    {
        if (HasActiveRecoveredCamera)
        {
            double heading = _rac1RuntimeCameraState!.ControlHeadingRadians;
            Vector3 recoveredForward = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
                Math.Cos(heading),
                Math.Sin(heading)).Normalized();
            Vector3 recoveredRight = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
                Math.Sin(heading),
                -Math.Cos(heading)).Normalized();
            return new PlayerPlanarBasis(
                recoveredRight.X,
                recoveredRight.Z,
                recoveredForward.X,
                recoveredForward.Z);
        }

        Vector3 sceneRight = _yaw.GlobalTransform.Basis * Vector3.Right;
        Vector3 sceneForward = _yaw.GlobalTransform.Basis * new Vector3(0f, 0f, -1f);
        sceneRight.Y = 0f;
        sceneForward.Y = 0f;
        sceneRight = sceneRight.Normalized();
        sceneForward = sceneForward.Normalized();
        return new PlayerPlanarBasis(
            sceneRight.X,
            sceneRight.Z,
            sceneForward.X,
            sceneForward.Z);
    }

    private static PlayerPlanarBasis GetRac1NativePlanarBasis() =>
        new(-1d, 0d, 0d, 1d);

    /// <summary>
    /// Apply the right stick to OBP's existing third-person camera. This is host
    /// policy only and deliberately does not claim the current rates/deadzone as
    /// recovered R&amp;C1 camera behaviour.
    /// </summary>
    private void ApplyGamepadCamera(float delta, Vector2 rawIntent)
    {
        float magnitude = rawIntent.Length();
        if (magnitude <= GamepadCameraDeadzone)
            return;

        Vector2 intent = magnitude > 1f ? rawIntent / magnitude : rawIntent;
        _yaw.RotateY(-intent.X * GamepadCameraYawRadiansPerSecond * delta);
        float pitch = Mathf.Clamp(
            _pitch.Rotation.X - intent.Y * GamepadCameraPitchRadiansPerSecond * delta,
            Mathf.DegToRad(-82f),
            Mathf.DegToRad(55f));
        _pitch.Rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Pull the third-person camera in when the line from the pivot to it is blocked by geometry.</summary>
    private void UpdateCameraDistance()
    {
        var pivot = _pitch.GlobalPosition;
        var want = _pitch.GlobalTransform * new Vector3(0, 0, _cameraDistance);
        var query = PhysicsRayQueryParameters3D.Create(pivot, want);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        float dist = _cameraDistance;
        if (hit.Count > 0)
        {
            dist = Mathf.Clamp(pivot.DistanceTo((Vector3)hit["position"]) - 0.3f, 1.2f, _cameraDistance);
        }

        Camera.Position = Camera.Position.Lerp(new Vector3(0, 0, dist), 0.35f);
    }

    /// <summary>
    /// Retune only the presentation camera for a real avatar while preserving the
    /// debug controller's collision and movement. This is an OBP camera choice,
    /// not a claim about the retail game's native camera constants.
    /// </summary>
    public void ConfigureAvatarPresentation(float avatarHeight)
    {
        if (!float.IsFinite(avatarHeight) || avatarHeight <= 0f)
        {
            return;
        }

        float height = Mathf.Clamp(avatarHeight, 0.8f, 3.5f);
        float clearance = Scripted ? 1.0f : 0.65f;
        float distanceScale = Scripted ? 5.5f : 4.5f;
        _cameraDistance = Mathf.Clamp(height * distanceScale, 4.5f, Scripted ? 10f : 8f);
        _pitch.Position = new Vector3(0f, height + clearance, 0f);
        if (!HasActiveRecoveredCamera)
            Camera.Position = new Vector3(0f, 0f, _cameraDistance);
    }
}
