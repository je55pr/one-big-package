using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

public static partial class RuntimeWorldScene
{
    // Deterministic animated mesh presentation.
    /// <summary>
    /// Pose every animated moby in <paramref name="result"/> for
    /// <paramref name="clockSeconds"/> of elapsed (unpaused) world time — call
    /// from the host tick. Deterministic in the clock value.
    /// </summary>
    public static void AdvanceAnimated(Result result, double clockSeconds)
    {
        if (result.AnimatedMeshes is { } animated)
        {
            foreach (var am in animated)
                am.Advance(clockSeconds);
        }

        if (result.DynamicObjectNodes is { } dynamic)
        {
            foreach (var node in dynamic)
                node.AdvanceAnimation(clockSeconds);
        }
    }


    private static (int Instances, int Triangles, IReadOnlyList<AnimatedMesh> Players) BuildAnimatedMeshes(
        RuntimeWorld world, Node3D root, WorldMaterialFactory mats)
    {
        int triangles = 0;
        // --- animated mobies: one CPU-skinned node per instance/texture group,
        // kept out of the merged static soup so it can swap frames over time ---
        int animatedMeshInstances = 0;
        var animatedPlayers = new System.Collections.Generic.List<AnimatedMesh>();
        foreach (var am in world.AnimatedMeshes ?? System.Array.Empty<RuntimeAnimatedMesh>())
        {
            if (am.Frames.Count == 0 || am.Indices.Length < 3 || am.VertexCount < 3)
            {
                continue;
            }

            // ToScene negates X (a reflection) — reverse every triangle's winding
            // to match, exactly as the static mesh loop does.
            var idx = new int[am.Indices.Length];
            for (int t = 0; t + 3 <= am.Indices.Length; t += 3)
            {
                idx[t] = am.Indices[t];
                idx[t + 1] = am.Indices[t + 2];
                idx[t + 2] = am.Indices[t + 1];
            }

            int vc = am.VertexCount;
            var uv = new Vector2[vc];
            for (int i = 0; i < vc && i * 2 + 1 < am.Uvs.Length; i++)
            {
                uv[i] = new Vector2(am.Uvs[i * 2], am.Uvs[i * 2 + 1]);
            }

            bool hasCol = am.Colors.Length == vc * 4;
            var col = hasCol ? new Color[vc] : null;
            for (int i = 0; hasCol && i < vc; i++)
            {
                col![i] = new Color(am.Colors[i * 4], am.Colors[i * 4 + 1], am.Colors[i * 4 + 2], am.Colors[i * 4 + 3]);
            }

            // Per-frame scene-space vertex arrays (X un-mirrored).
            var frames = new Vector3[am.Frames.Count][];
            for (int f = 0; f < am.Frames.Count; f++)
            {
                var src = am.Frames[f];
                var dst = new Vector3[vc];
                for (int v = 0; v < vc; v++)
                {
                    dst[v] = ToScene(src[v * 3], src[v * 3 + 1], src[v * 3 + 2]);
                }

                frames[f] = dst;
            }

            var mat = mats.Instanced(am.AssetKind, am.TextureId, hasCol, am.MaterialPresentation);

            var animMesh = new ArrayMesh();
            var mi = new MeshInstance3D
            {
                Name = am.Name,
                Mesh = animMesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(mi);
            animatedPlayers.Add(new AnimatedMesh(mi, animMesh, frames, idx, uv, col, am.FramesPerSecond, am.Name));
            animatedMeshInstances++;
            triangles += am.TriangleCount;
        }

        return (animatedMeshInstances, triangles, animatedPlayers);
    }
}
