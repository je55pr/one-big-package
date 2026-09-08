using Godot;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

/// <summary>
/// Resolves a screen-space pick against a built <see cref="RuntimeWorldScene.Result"/>:
/// a ray from the camera against every visible render <see cref="MeshInstance3D"/>'s
/// world AABB, nearest entry wins, then the hit is turned into a neutral
/// <see cref="WorldHit"/> (asset kind + texture id from the
/// <c>"{kind}_{textureId}"</c> name, the owning <c>dyn_*</c>
/// <see cref="RuntimeDynamicObject"/>, or the matching
/// <see cref="RuntimeAnimatedMesh"/>). The engine-neutral readout is built by
/// <see cref="WorldObjectDescriptorBuilder"/>.
/// </summary>
public static class WorldPicker
{
    public static WorldHit? Pick(
        Camera3D camera,
        Vector2 screenPoint,
        RuntimeWorldScene.Result result,
        RuntimeWorld world,
        System.Func<string, WorldHost.AnimationState?>? animationState = null)
    {
        if (result.Root is not { } root || !GodotObject.IsInstanceValid(root))
        {
            return null;
        }

        var origin = camera.ProjectRayOrigin(screenPoint);
        var dir = camera.ProjectRayNormal(screenPoint);

        MeshInstance3D? best = null;
        float bestT = float.MaxValue;
        foreach (var mi in Meshes(root))
        {
            if (!mi.Visible || mi.Mesh is null || mi.Name.ToString().StartsWith("obp_", System.StringComparison.Ordinal))
            {
                continue;
            }

            var aabb = mi.GlobalTransform * mi.GetAabb();
            if (RayAabb(origin, dir, aabb, out float t) && t < bestT)
            {
                bestT = t;
                best = mi;
            }
        }

        if (best is null)
        {
            return null;
        }

        Vector3 point = origin + (dir * bestT);
        string name = best.Name;
        var (kind, textureId) = ParseName(name);

        // Owning dynamic object: a "dyn_*" ancestor mapped back to its source.
        for (var n = best.GetParent(); n is Node3D; n = n.GetParent())
        {
            foreach (var dyn in result.DynamicObjectNodes ?? System.Array.Empty<RuntimeWorldScene.DynamicObjectNode>())
            {
                if (dyn.Root == n)
                {
                    return new WorldHit(kind, textureId, Obp(point), null, dyn.Source);
                }
            }
        }

        // A matching animated mesh (named by RuntimeAnimatedMesh.Name).
        foreach (var am in result.AnimatedMeshes ?? System.Array.Empty<AnimatedMesh>())
        {
            if (am.Instance == best)
            {
                var runtimeAnim = Find(world, am.Name);
                var state = animationState?.Invoke(am.Name);
                AnimationReadout? readout = state is { } s
                    ? new AnimationReadout(s.Name, s.FramesPerSecond, s.FrameCount, s.CurrentFrame, s.Playing, runtimeAnim?.Skeleton is not null)
                    : null;
                return new WorldHit(kind, textureId, Obp(point), null, null, runtimeAnim, readout);
            }
        }

        return new WorldHit(kind, textureId, Obp(point));
    }

    private static RuntimeAnimatedMesh? Find(RuntimeWorld world, string name)
    {
        foreach (var a in world.AnimatedMeshes ?? System.Array.Empty<RuntimeAnimatedMesh>())
        {
            if (a.Name == name)
            {
                return a;
            }
        }

        return null;
    }

    /// <summary>Godot scene point → OBP space (un-mirror X — the inverse of <see cref="RuntimeWorldScene.ToScene"/>).</summary>
    private static OBP.Core.Math.Vec3 Obp(Vector3 p) => new(-p.X, p.Y, p.Z);

    private static (string Kind, int TextureId) ParseName(string name)
    {
        int u = name.LastIndexOf('_');
        if (u > 0 && int.TryParse(name.AsSpan(u + 1), out int id))
        {
            return (name[..u], id);
        }

        return (name, 0);
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                yield return mi;
            }

            foreach (var nested in Meshes(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Slab ray/AABB. <paramref name="t"/> is the entry distance (0 if the origin is inside).</summary>
    private static bool RayAabb(Vector3 origin, Vector3 dir, Aabb box, out float t)
    {
        t = 0f;
        var min = box.Position;
        var max = box.End;
        float tMin = 0f, tMax = float.MaxValue;

        for (int a = 0; a < 3; a++)
        {
            float o = origin[a], d = dir[a], lo = min[a], hi = max[a];
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi)
                {
                    return false;
                }
            }
            else
            {
                float inv = 1f / d;
                float t1 = (lo - o) * inv;
                float t2 = (hi - o) * inv;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }

                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax)
                {
                    return false;
                }
            }
        }

        t = tMin;
        return true;
    }
}
