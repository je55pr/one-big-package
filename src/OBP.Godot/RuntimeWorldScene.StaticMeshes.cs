using Godot;
using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Godot;

public static partial class RuntimeWorldScene
{
    // Static, sky and presentation-group mesh construction.
    private static (int MeshInstances, int Triangles, int SkyMeshes) BuildStaticMeshes(
        RuntimeWorld world, Node3D root, Node3D skyRoot, Options options, WorldMaterialFactory mats)
    {
        var presentationGroups = new System.Collections.Generic.Dictionary<(Node3D Parent, string Group), Node3D>();

        int meshInstances = 0;
        int triangles = 0;
        int skyMeshes = 0;

        foreach (var m in world.Meshes)
        {
            if (options.OnlyAnimatedMobies)
            {
                break;
            }

            if (m.Indices.Length < 3 || m.Positions.Length < 9)
            {
                continue;
            }

            bool isSky = m.AssetKind == "sky";
            if (isSky && !options.IncludeSky)
            {
                continue;
            }

            // Missing texture is not normally permission to render a sky surface —
            // without its per-vertex colours a bare shell renders as a flat faceted
            // blob that swallows the view. A source importer may explicitly
            // authorise a materialless surface when retail evidence supplies its
            // presentation semantics (UYA's gouraud backdrop with importer-provided
            // vertex colour); the camera-followed textured cloud layers stay, and
            // the background clear colour is the backdrop.
            if (isSky &&
                mats.TextureFor(m.AssetKind, m.TextureId) is null &&
                !m.RenderWithoutTexture)
            {
                continue;
            }

            if (m.AssetKind == "death-plane" && !options.IncludeDeathPlane)
            {
                continue;
            }

            if (m.AssetKind == "moby-marker" && !options.IncludeMobyMarkers)
            {
                continue;
            }

            int vertexCount = m.Positions.Length / 3;
            var verts = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                verts[i] = ToScene(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            }

            // ToScene negates X — a reflection — which reverses every triangle's
            // winding. Swap two corners back so the reflection is undone and
            // face orientation matches the source data (all world materials are
            // two-sided, so this is about keeping normals sane, not culling).
            var indices = new int[m.Indices.Length];
            for (int t = 0; t + 3 <= m.Indices.Length; t += 3)
            {
                indices[t] = m.Indices[t];
                indices[t + 1] = m.Indices[t + 2];
                indices[t + 2] = m.Indices[t + 1];
            }

            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            bool hasUv = m.Uvs.Length == vertexCount * 2;
            if (hasUv)
            {
                var uv = new Vector2[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
                }

                arrays[(int)Mesh.ArrayType.TexUV] = uv;
            }

            bool hasColor = m.Colors is { } cc && cc.Length == vertexCount * 4;
            if (hasColor)
            {
                // tfrag baked colours are the level's static lighting in a dark
                // gamma space — MaterialModel.BakeCurve lifts them so shadowed
                // terrain doesn't crush and lit terrain still lifts. Moby vertex
                // colours are an already-linear normal shade — passed straight.
                bool bake = MaterialModel.UsesBakeCurve(m.AssetKind);
                var col = new Color[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    float r = m.Colors![i * 4], g = m.Colors[i * 4 + 1], b = m.Colors[i * 4 + 2];
                    if (bake)
                    {
                        r = MaterialModel.BakeCurve(r);
                        g = MaterialModel.BakeCurve(g);
                        b = MaterialModel.BakeCurve(b);
                    }

                    col[i] = new Color(r, g, b, m.Colors[i * 4 + 3]);
                }

                arrays[(int)Mesh.ArrayType.Color] = col;
            }

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var mat = mats.StaticMesh(m.AssetKind, m.TextureId, hasUv, hasColor, isSky, m.RenderWithoutTexture, m.TriangleCount, m.MaterialPresentation);

            var mi = new MeshInstance3D
            {
                Name = $"{m.AssetKind}_{m.TextureId}",
                Mesh = mesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };

            Node3D parent = isSky ? skyRoot : root;
            if (m.PresentationGroup is { Length: > 0 } group)
            {
                var key = (parent, group);
                if (!presentationGroups.TryGetValue(key, out var groupNode))
                {
                    groupNode = new Node3D { Name = PresentationGroupNodeName(group) };
                    parent.AddChild(groupNode);
                    presentationGroups[key] = groupNode;
                }
                parent = groupNode;
            }

            if (isSky)
            {
                mi.Layers = 1;
                skyMeshes++;
            }
            parent.AddChild(mi);

            meshInstances++;
            triangles += m.TriangleCount;
        }

        if (skyMeshes > 0)
        {
            root.AddChild(skyRoot);
        }
        else
        {
            // Never parented (no sky, or IncludeSky = false) — free it now so it
            // isn't left as an orphan node for the lifetime of the process.
            skyRoot.Free();
        }

        return (meshInstances, triangles, skyMeshes);
    }
}
