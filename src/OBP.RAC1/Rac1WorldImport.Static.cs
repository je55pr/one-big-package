using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static void AddPlacedStaticGeometry(
        Rac1LevelCore.Core core,
        byte[] gameplayBytes,
        List<RuntimeMesh> meshes,
        List<RuntimeTexture> textures,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        var classes = Rac1StaticClasses.Read(core);
        var instances = Rac1Instances.Parse(gameplayBytes);

        var tieTextures = ReadRuntimeTextures(core, "tie", core.Header.TieTextures);
        var shrubTextures = ReadRuntimeTextures(core, "shrub", core.Header.ShrubTextures);
        var mobyTextures = ReadRuntimeTextures(core, "moby", core.Header.MobyTextures);
        textures.AddRange(tieTextures);
        textures.AddRange(shrubTextures);
        textures.AddRange(mobyTextures);

        var tieTextureIds = tieTextures.Select(t => t.TextureId).ToHashSet();
        var shrubTextureIds = shrubTextures.Select(t => t.TextureId).ToHashSet();
        var mobyTextureIds = mobyTextures.Select(t => t.TextureId).ToHashSet();

        PlaceMatrixInstances(
            "tie",
            instances.TieInstances.Select(i => (i.OClass, i.Matrix)),
            oClass => classes.Ties.TryGetValue(oClass, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds)
                : null,
            tieTextureIds,
            meshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);

        PlaceMatrixInstances(
            "shrub",
            instances.ShrubInstances.Select(i => (i.OClass, i.Matrix)),
            oClass => classes.Shrubs.TryGetValue(oClass, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds)
                : null,
            shrubTextureIds,
            meshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);

        PlaceMobyInstances(
            instances.MobyInstances,
            classes.Mobies,
            mobyTextureIds,
            meshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);
    }

    private static List<RuntimeTexture> ReadRuntimeTextures(
        Rac1LevelCore.Core core,
        string kind,
        Rac1LevelCore.ArrayRange range)
    {
        var decoded = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(range.Count, range.Offset));
        return decoded
            .Select(t => new RuntimeTexture(kind, t.Index, t.Width, t.Height, t.Rgba))
            .ToList();
    }

    private static void PlaceMatrixInstances(
        string kind,
        IEnumerable<(int OClass, float[] Matrix)> instances,
        Func<int, (double[] Positions, float[] Uvs, int[] Indices, int[] TriangleTextureIds)?> lookup,
        HashSet<int> textureIds,
        List<RuntimeMesh> meshes,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        var byTexture = new Dictionary<int, (
            List<double> Positions,
            List<float> Uvs,
            List<int> Indices,
            Dictionary<(double X, double Y, double Z, double S, double T), int> Weld)>();

        foreach (var instance in instances)
        {
            var cls = lookup(instance.OClass)
                ?? throw new InvalidDataException($"R&C1 {kind} placement references missing class {instance.OClass}.");
            if (cls.Indices.Length != cls.TriangleTextureIds.Length * 3)
            {
                throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} triangle/material arrays disagree.");
            }
            if (cls.Uvs.Length != cls.Positions.Length / 3 * 2)
            {
                throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} UV count is invalid.");
            }

            var worldPositions = new double[cls.Positions.Length];
            for (int i = 0; i < cls.Positions.Length; i += 3)
            {
                var (nx, ny, nz) = Rac1Instances.TransformPoint(
                    instance.Matrix,
                    cls.Positions[i],
                    cls.Positions[i + 1],
                    cls.Positions[i + 2]);
                double x = R2(nx), y = R2(nz), z = R2(ny);
                worldPositions[i] = x;
                worldPositions[i + 1] = y;
                worldPositions[i + 2] = z;
                minX = System.Math.Min(minX, nx);
                minY = System.Math.Min(minY, nz);
                minZ = System.Math.Min(minZ, ny);
                maxX = System.Math.Max(maxX, nx);
                maxY = System.Math.Max(maxY, nz);
                maxZ = System.Math.Max(maxZ, ny);
            }

            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                int textureId = cls.TriangleTextureIds[face];
                if (textureId < 0 || !textureIds.Contains(textureId))
                {
                    throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} references missing texture {textureId}.");
                }
                if (!byTexture.TryGetValue(textureId, out var group))
                {
                    byTexture[textureId] = group = (
                        [], [], [],
                        new Dictionary<(double, double, double, double, double), int>());
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertex = cls.Indices[face * 3 + corner];
                    if (sourceVertex < 0 || sourceVertex * 3 + 2 >= worldPositions.Length)
                    {
                        throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} has an invalid index {sourceVertex}.");
                    }
                    double x = worldPositions[sourceVertex * 3];
                    double y = worldPositions[sourceVertex * 3 + 1];
                    double z = worldPositions[sourceVertex * 3 + 2];
                    double s = R4(cls.Uvs[sourceVertex * 2]);
                    double t = R4(cls.Uvs[sourceVertex * 2 + 1]);
                    var key = (x, y, z, s, t);
                    if (!group.Weld.TryGetValue(key, out int runtimeVertex))
                    {
                        runtimeVertex = group.Positions.Count / 3;
                        group.Weld[key] = runtimeVertex;
                        group.Positions.Add(x);
                        group.Positions.Add(y);
                        group.Positions.Add(z);
                        group.Uvs.Add((float)s);
                        group.Uvs.Add((float)t);
                    }
                    group.Indices.Add(runtimeVertex);
                }
            }
        }

        foreach (var (textureId, group) in byTexture.OrderBy(kv => kv.Key))
        {
            meshes.Add(new RuntimeMesh(
                kind,
                textureId,
                group.Positions.ToArray(),
                group.Uvs.ToArray(),
                group.Indices.ToArray()));
        }
    }
}
