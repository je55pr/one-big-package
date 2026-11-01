using Godot;
using OBP.Godot;
using OBP.Runtime;

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
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        if (anim is { Count: > 0 })
        {
            // Only the first instance is framed, not the entire moby spread.
            string firstInstance = anim[0].Name.Split("_t")[0];
            foreach (var am in anim.Where(a => a.Name.StartsWith(firstInstance)))
            {
                foreach (var frame in am.Frames)
                {
                    for (int i = 0; i + 3 <= frame.Length; i += 3)
                    {
                        minX = System.Math.Min(minX, frame[i]);
                        maxX = System.Math.Max(maxX, frame[i]);
                        minY = System.Math.Min(minY, frame[i + 1]);
                        maxY = System.Math.Max(maxY, frame[i + 1]);
                        minZ = System.Math.Min(minZ, frame[i + 2]);
                        maxZ = System.Math.Max(maxZ, frame[i + 2]);
                    }
                }
            }
        }
        else
        {
            minX = minY = minZ = -1;
            maxX = maxY = maxZ = 1;
        }

        var bounds = new OBP.Core.Math.ObpBounds(
            new OBP.Core.Math.Vec3(minX, minY, minZ),
            new OBP.Core.Math.Vec3(maxX, maxY, maxZ));
        RuntimeWorldScene.FrameCamera(camera, bounds, azimuthDegrees: 35f, elevationDegrees: 18f);
        var centre = RuntimeWorldScene.ToScene((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
        camera.Position = centre + (camera.Position - centre) * 0.55f;
        camera.LookAt(centre, Vector3.Up);
        camera.Current = true;
        GD.Print($"[OBPGame] anim-solo camera at {camera.Position}, {anim?.Count ?? 0} animated mobies");
    }
}
