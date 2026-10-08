using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.RAC2.Audio;
using OBP.RAC2.Geometry;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;

namespace OBP.RAC2;

/// <summary>
/// Native world assembly for a Going Commando retail level — the C# equivalent of
/// <c>reference-ts/tools/gc-world.mjs</c>. Opens the level WAD from an ISO,
/// decodes chunk-0 tfrags + octree collision, instances the tie / shrub / moby
/// classes (per-texture grouping + vertex welding, native Z-up → OBP Y-up), adds
/// moby instance markers, the sky shells and a death-height plane, and grows the
/// world bounds.
///
/// <para>
/// Output is a neutral <see cref="RuntimeWorld"/> — no GC or Godot types cross
/// this boundary. This is where GC-specific data conversion terminates.
/// </para>
public static partial class GcWorldImport
{
    private static IEnumerable<RuntimeMesh> ToTfragMeshes(GcTfrag.Mesh mesh)
    {
        var byTex = new Dictionary<int, List<int>>();
        for (int f = 0; f < mesh.TriangleTextureIds.Length; f++)
        {
            int tex = mesh.TriangleTextureIds[f];
            if (!byTex.TryGetValue(tex, out var list))
            {
                byTex[tex] = list = [];
            }

            list.Add(mesh.Indices[f * 3]);
            list.Add(mesh.Indices[f * 3 + 1]);
            list.Add(mesh.Indices[f * 3 + 2]);
        }

        bool haveColours = mesh.Colors.Length == mesh.Positions.Length;
        bool haveAlpha = mesh.VertexAlpha.Length == mesh.Positions.Length / 3;
        foreach (var (tex, tris) in byTex.OrderBy(kv => kv.Key))
        {
            var remap = new Dictionary<int, int>();
            var positions = new List<double>();
            var uvs = new List<float>();
            var colours = new List<float>();
            var indices = new List<int>();
            foreach (int v in tris)
            {
                if (!remap.TryGetValue(v, out int nv))
                {
                    nv = positions.Count / 3;
                    remap[v] = nv;
                    positions.Add(mesh.Positions[v * 3]);
                    positions.Add(mesh.Positions[v * 3 + 1]);
                    positions.Add(mesh.Positions[v * 3 + 2]);
                    uvs.Add(mesh.Uvs[v * 2]);
                    uvs.Add(mesh.Uvs[v * 2 + 1]);
                    // Baked tfrag vertex colour (PS2 lighting/AO), faithful /255.
                    // The PS2 GS doubles it at raster time (0x80 == 1.0); the
                    // scene builder applies that 2x when it consumes these.
                    if (haveColours)
                    {
                        colours.Add(mesh.Colors[v * 3]);
                        colours.Add(mesh.Colors[v * 3 + 1]);
                        colours.Add(mesh.Colors[v * 3 + 2]);
                        colours.Add(haveAlpha ? mesh.VertexAlpha[v] : 1f);
                    }
                }

                indices.Add(nv);
            }

            yield return new RuntimeMesh("tfrag", tex, positions.ToArray(), uvs.ToArray(), indices.ToArray(),
                haveColours ? colours.ToArray() : null);
        }
    }

}
