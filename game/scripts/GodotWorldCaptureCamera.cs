using Godot;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Camera;

namespace OBP.Game;

/// <summary>
/// Presentation-only framing for authored world and animated-moby capture shots.
/// Owns no camera lifecycle, player state, or source-world tick.
/// </summary>
internal static class GodotWorldCaptureCamera
{
    /// <summary>Frame a planet from above its trusted geometry, without physics settling.</summary>
    internal static void FrameShowcase(Camera3D camera, RuntimeWorld world)
    {
        RuntimeWorldScene.FrameCamera(camera, world.Bounds, azimuthDegrees: 38f, elevationDegrees: 26f);
        // Pull back slightly and aim above centre so the horizon is visible.
        var b = world.Bounds;
        var centre = RuntimeWorldScene.ToScene(
            (b.Min.X + b.Max.X) * 0.5, (b.Min.Y + b.Max.Y) * 0.5, (b.Min.Z + b.Max.Z) * 0.5);
        camera.Position = centre + (camera.Position - centre) * 1.15f;
        camera.LookAt(centre + Vector3.Up * (float)((b.Max.Y - b.Min.Y) * 0.15), Vector3.Up);
        camera.Current = true;
        GD.Print($"[OBPGame] showcase camera at {camera.Position}");
    }

    /// <summary>Frame the first authored animated-moby instance for --anim-solo.</summary>
    internal static void FrameAnimated(Camera3D camera, RuntimeWorld world)
    {
        var anim = world.AnimatedMeshes;
        var measured = AnimatedMeshFraming.MeasureFirstInstance(anim);
        if (measured.Bounds is not { } bounds)
        {
            // Explicit failure: never hand invalid bounds to the camera; leave it untouched.
            GD.PushError($"[OBPGame] anim-solo camera not framed ({measured.Status}): {measured.Detail}");
            return;
        }

        RuntimeWorldScene.FrameCamera(camera, bounds, azimuthDegrees: 35f, elevationDegrees: 18f);
        var centre = RuntimeWorldScene.ToScene(bounds.Center.X, bounds.Center.Y, bounds.Center.Z);
        camera.Position = centre + (camera.Position - centre) * 0.55f;
        camera.LookAt(centre, Vector3.Up);
        camera.Current = true;
        GD.Print($"[OBPGame] anim-solo camera at {camera.Position}, {measured.MeshCount} meshes of {measured.Instance}");
    }
}
