using Godot;
using OBP.Runtime.Camera;

namespace OBP.Godot.Camera;

/// <summary>
/// Projects an engine-neutral camera state into the handedness-corrected Godot scene.
/// </summary>
public static class RuntimeCameraSceneAdapter
{
    public readonly record struct ScenePose(
        Vector3 Eye,
        Vector3 Forward,
        Vector3 Up);

    public static ScenePose ToScenePose(RuntimeCameraState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        Vector3 eye = RuntimeWorldScene.ToScene(
            state.Eye.X,
            state.Eye.Y,
            state.Eye.Z);
        Vector3 forward = RuntimeWorldScene.ToScene(
            state.Forward.X,
            state.Forward.Y,
            state.Forward.Z);

        if (forward.LengthSquared() <= 1e-12f)
            throw new ArgumentOutOfRangeException(nameof(state), "Camera forward vector must be non-zero.");

        Vector3 up = state.Up is { } orientedUp
            ? RuntimeWorldScene.ToScene(orientedUp.X, orientedUp.Y, orientedUp.Z) : Vector3.Up;
        if (up.LengthSquared() <= 1e-12f || forward.Cross(up).LengthSquared() <= 1e-12f)
            throw new ArgumentOutOfRangeException(nameof(state), "Camera up must be non-zero and independent of forward.");
        return new ScenePose(eye, forward.Normalized(), up.Normalized());
    }

    public static void Apply(Camera3D camera, RuntimeCameraState state)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ScenePose pose = ToScenePose(state);
        if (state.VerticalFieldOfViewRadians is { } fov)
        {
            camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            camera.Fov = (float)(fov * 180d / Math.PI);
        }
        camera.LookAtFromPosition(
            pose.Eye,
            pose.Eye + pose.Forward,
            pose.Up);
    }
}
