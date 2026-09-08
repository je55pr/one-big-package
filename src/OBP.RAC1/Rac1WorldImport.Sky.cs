using OBP.PS2.Geometry;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static double R2(double v) => System.Math.Floor(v * 100 + 0.5) / 100 + 0.0;
    private static double R4(double v) => System.Math.Floor(v * 10000 + 0.5) / 10000 + 0.0;

    /// <summary>
    /// Add the retail sky as camera-centred shell geometry. Returns the number
    /// of non-texture runtime materials contributed by the sky.
    /// </summary>
    private static int AddSky(
        Rac1LevelCore.Core core,
        double levelRadius,
        List<RuntimeMesh> meshes,
        List<RuntimeTexture> textures)
    {
        int skyOffset = core.Header.SkyOffset;
        int skyEnd = core.Header.CollisionOffset;
        if (skyOffset <= 0)
        {
            return 0;
        }
        if (skyEnd <= skyOffset || skyEnd > core.Assets.Length)
        {
            throw new InvalidDataException("R&C1 sky section boundaries are invalid.");
        }
        var sky = RcSky.Read(core.Assets.AsSpan(skyOffset, skyEnd - skyOffset).ToArray());
        if (sky.Shells.Count == 0)
        {
            return 0;
        }

        foreach (var texture in sky.Textures)
        {
            textures.Add(new RuntimeTexture(
                "sky",
                texture.Index,
                texture.Width,
                texture.Height,
                texture.Rgba));
        }

        double shellMax = 0;
        foreach (var shell in sky.Shells)
        {
            foreach (double p in shell.Positions)
            {
                shellMax = System.Math.Max(shellMax, System.Math.Abs(p));
            }
        }

        double domeRadius = System.Math.Max(levelRadius * 3.0, 2000.0);
        double scale = shellMax > 0 ? domeRadius / shellMax : 1;
        foreach (var shell in sky.Shells)
        {
            AddSkyShell(shell, scale, meshes);
        }

        // One shared gouraud/untextured material in addition to decoded textures.
        return 1;
    }
    private static void AddSkyShell(RcSky.Shell shell, double scale, List<RuntimeMesh> meshes)
    {
        var byTex = new Dictionary<int, (List<double> P, List<float> U, List<float> C, List<int> I,
            Dictionary<(double, double, double, double, double, double), int> Weld)>();
        var order = new List<int>();

        for (int face = 0; face < shell.TriangleTextureIds.Length; face++)
        {
            int textureId = shell.TriangleTextureIds[face];
            if (!byTex.TryGetValue(textureId, out var group))
            {
                byTex[textureId] = group = ([], [], [], [],
                    new Dictionary<(double, double, double, double, double, double), int>());
                order.Add(textureId);
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int sourceVertex = shell.Indices[face * 3 + corner];
                double lx = shell.Positions[sourceVertex * 3] * scale;
                double ly = shell.Positions[sourceVertex * 3 + 1] * scale;
                double lz = shell.Positions[sourceVertex * 3 + 2] * scale;
                double px = R2(lx), py = R2(lz), pz = R2(ly);
                double s = R4(shell.Uvs[sourceVertex * 2]);
                double t = R4(shell.Uvs[sourceVertex * 2 + 1]);
                double alpha = R2(shell.Alpha[sourceVertex]);
                var key = (px, py, pz, s, t, alpha);
                if (!group.Weld.TryGetValue(key, out int runtimeVertex))
                {
                    runtimeVertex = group.P.Count / 3;
                    group.Weld[key] = runtimeVertex;
                    group.P.Add(px);
                    group.P.Add(py);
                    group.P.Add(pz);
                    group.U.Add((float)s);
                    group.U.Add((float)t);
                    float av = (float)System.Math.Clamp(alpha, 0.0, 1.0);
                    group.C.Add(1f);
                    group.C.Add(1f);
                    group.C.Add(1f);
                    group.C.Add(av);
                }

                group.I.Add(runtimeVertex);
            }
        }

        foreach (int textureId in order)
        {
            var group = byTex[textureId];
            meshes.Add(new RuntimeMesh(
                "sky",
                textureId,
                group.P.ToArray(),
                group.U.ToArray(),
                group.I.ToArray(),
                group.C.ToArray()));
        }
    }
}
