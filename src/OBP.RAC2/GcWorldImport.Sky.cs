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
    private static void AddSkyShell(GcSky.Shell shell, double scale, List<RuntimeMesh> meshes)
    {
        var byTex = new Dictionary<int, (List<double> P, List<float> U, List<float> C, List<int> I, Dictionary<(double, double, double, double, double, double), int> Weld)>();
        var order = new List<int>();

        for (int f = 0; f < shell.TriangleTextureIds.Length; f++)
        {
            int tex = shell.TriangleTextureIds[f];
            if (!byTex.TryGetValue(tex, out var g))
            {
                byTex[tex] = g = ([], [], [], [], new Dictionary<(double, double, double, double, double, double), int>());
                order.Add(tex);
            }

            for (int k = 0; k < 3; k++)
            {
                int vi = shell.Indices[f * 3 + k];
                double lx = shell.Positions[vi * 3] * scale;
                double ly = shell.Positions[vi * 3 + 1] * scale;
                double lz = shell.Positions[vi * 3 + 2] * scale;
                // native Z-up -> OBP Y-up, centred on origin (see AddSkyShell caller).
                double px = R2(lx), py = R2(lz), pz = R2(ly);
                double s = R4(shell.Uvs[vi * 2]), t = R4(shell.Uvs[vi * 2 + 1]);
                double a = R2(shell.Alpha[vi]);
                var key = (px, py, pz, s, t, a);
                if (!g.Weld.TryGetValue(key, out int idx))
                {
                    idx = g.P.Count / 3;
                    g.Weld[key] = idx;
                    g.P.Add(px);
                    g.P.Add(py);
                    g.P.Add(pz);
                    g.U.Add((float)s);
                    g.U.Add((float)t);
                    float av = (float)System.Math.Clamp(a, 0.0, 1.0);
                    g.C.Add(1f);
                    g.C.Add(1f);
                    g.C.Add(1f);
                    g.C.Add(av);
                }

                g.I.Add(idx);
            }
        }

        foreach (int tex in order)
        {
            var g = byTex[tex];
            meshes.Add(new RuntimeMesh("sky", tex, g.P.ToArray(), g.U.ToArray(), g.I.ToArray(), g.C.ToArray()));
        }
    }
}
