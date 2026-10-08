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
    private static void PlaceMatrixInstances(
        string kind,
        IReadOnlyList<GcInstances.MatrixInstance> instances,
        Func<int, (double[] Positions, float[] Uvs, int[] Indices, int[] TriTexIds, RuntimeMaterialPresentation?[] TriPresentations)?> lookup,
        List<RuntimeMesh> meshes,
        Action<double, double, double> grow,
        bool growBounds)
    {
        var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation),
            (List<double> P, List<float> U, List<int> I, Dictionary<(double, double, double, double, double), int> Weld)>();

        foreach (var inst in instances)
        {
            var cls = lookup(inst.OClass);
            if (cls is not { } c || c.Indices.Length == 0)
            {
                continue;
            }

            var m = inst.Matrix;
            var worldPos = new double[c.Positions.Length];
            for (int i = 0; i < c.Positions.Length; i += 3)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, c.Positions[i], c.Positions[i + 1], c.Positions[i + 2]);
                worldPos[i] = R2(x);
                worldPos[i + 1] = R2(z);
                worldPos[i + 2] = R2(y);
                if (growBounds)
                {
                    grow(x, z, y);
                }
            }

            for (int f = 0; f < c.TriTexIds.Length; f++)
            {
                int tex = c.TriTexIds[f];
                var presentation = (uint)f < (uint)c.TriPresentations.Length
                    ? c.TriPresentations[f]
                    : null;
                var surface = (tex, presentation);
                if (!bySurface.TryGetValue(surface, out var g))
                {
                    bySurface[surface] = g = ([], [], [], new Dictionary<(double, double, double, double, double), int>());
                }

                for (int k = 0; k < 3; k++)
                {
                    int vi = c.Indices[f * 3 + k];
                    double px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
                    float s = c.Uvs[vi * 2], t = c.Uvs[vi * 2 + 1];
                    double rs = R4(s), rt = R4(t);
                    var key = (px, py, pz, rs, rt);
                    if (!g.Weld.TryGetValue(key, out int idx))
                    {
                        idx = g.P.Count / 3;
                        g.Weld[key] = idx;
                        g.P.Add(px);
                        g.P.Add(py);
                        g.P.Add(pz);
                        g.U.Add((float)rs);
                        g.U.Add((float)rt);
                    }

                    g.I.Add(idx);
                }
            }
        }

        foreach (var (surface, g) in bySurface
                     .OrderBy(kv => kv.Key.TextureId)
                     .ThenBy(kv => kv.Key.Presentation?.ToString(), StringComparer.Ordinal))
        {
            meshes.Add(new RuntimeMesh(
                kind, surface.TextureId, g.P.ToArray(), g.U.ToArray(), g.I.ToArray(),
                MaterialPresentation: surface.Presentation));
        }
    }

    private static (double X, double Y, double Z) YUp((float X, float Y, float Z) v) => (v.X, v.Z, v.Y);

    private static double Dist2((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Class-local normal -> unit world normal in OBP Y-up space (rotate by the instance matrix 3x3, swap y/z, normalise).</summary>
    private static (double X, double Y, double Z) WorldNormal(double[] m, double nx, double ny, double nz)
    {
        double wx = m[0] * nx + m[4] * ny + m[8] * nz;
        double wy = m[1] * nx + m[5] * ny + m[9] * nz;
        double wz = m[2] * nx + m[6] * ny + m[10] * nz;
        double len = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);
        return len < 1e-9 ? (0, 1, 0) : (wx / len, wz / len, wy / len);
    }

    private static readonly int[][] MarkerCube =
    [
        [-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1], [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1],
    ];

    private static readonly int[][] MarkerFaces =
    [
        [0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 4, 5], [0, 5, 1],
        [1, 5, 6], [1, 6, 2], [2, 6, 7], [2, 7, 3], [3, 7, 4], [3, 4, 0],
    ];

}
