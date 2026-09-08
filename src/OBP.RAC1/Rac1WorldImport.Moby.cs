using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static void PlaceMobyInstances(
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
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
            if (!classes.TryGetValue(instance.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }
            var mesh = cls.Mesh;
            if (mesh.Indices.Length != cls.TriangleTextureIds.Length * 3)
            {
                throw new InvalidDataException($"R&C1 moby class {instance.OClass} triangle/material arrays disagree.");
            }
            if (mesh.Uvs.Length != mesh.Positions.Length / 3 * 2)
            {
                throw new InvalidDataException($"R&C1 moby class {instance.OClass} UV count is invalid.");
            }

            var worldPositions = new double[mesh.Positions.Length];
            for (int i = 0; i < mesh.Positions.Length; i += 3)
            {
                var (nx, ny, nz) = Rac1Instances.TransformMobyPoint(
                    instance,
                    mesh.Positions[i],
                    mesh.Positions[i + 1],
                    mesh.Positions[i + 2]);
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
                if (textureId >= 0 && !textureIds.Contains(textureId))
                {
                    throw new InvalidDataException($"R&C1 moby class {instance.OClass} references missing texture {textureId}.");
                }
                if (!byTexture.TryGetValue(textureId, out var group))
                {
                    byTexture[textureId] = group = (
                        [], [], [],
                        new Dictionary<(double, double, double, double, double), int>());
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertex = mesh.Indices[face * 3 + corner];
                    if (sourceVertex < 0 || sourceVertex * 3 + 2 >= worldPositions.Length)
                    {
                        throw new InvalidDataException($"R&C1 moby class {instance.OClass} has invalid index {sourceVertex}.");
                    }
                    double x = worldPositions[sourceVertex * 3];
                    double y = worldPositions[sourceVertex * 3 + 1];
                    double z = worldPositions[sourceVertex * 3 + 2];
                    double s = R4(mesh.Uvs[sourceVertex * 2]);
                    double t = R4(mesh.Uvs[sourceVertex * 2 + 1]);
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
                "moby",
                textureId,
                group.Positions.ToArray(),
                group.Uvs.ToArray(),
                group.Indices.ToArray()));
        }
    }
}
