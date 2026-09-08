using OBP.RAC1.Animation;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static List<RuntimeAnimatedMesh> BuildAnimatedMobyMeshes(
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
        HashSet<int> textureIds,
        out HashSet<int> animatedInstanceIndices,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ)
    {
        const int ProvenAnimatedClass = 1134;
        const int ProvenSequence = 1;
        const float NtscUpdateHz = 60f;
        animatedInstanceIndices = [];
        var output = new List<RuntimeAnimatedMesh>();

        foreach (var instance in instances.Where(i => i.OClass == ProvenAnimatedClass))
        {
            if (!classes.TryGetValue(instance.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
                throw new InvalidDataException("R&C1 class 1134 retail animation surface is missing.");
            var rest = cls.Sequences.Single(s => s.Index == 0).Value
                ?? throw new InvalidDataException("R&C1 class 1134 rest sequence is absent.");
            var sequence = cls.Sequences.Single(s => s.Index == ProvenSequence).Value
                ?? throw new InvalidDataException("R&C1 class 1134 animated sequence is absent.");
            if (rest.Frames.Count != 1 ||
                !Rac1MobyPose.CanPoseSingleJointRigid(cls.Mesh, cls.Joints, rest.Frames[0]) ||
                !Rac1MobyPose.IsRestAnchor(cls.Joints[0], rest.Frames[0]))
                throw new InvalidDataException("R&C1 class 1134 is outside the proven one-joint rest-anchor subset.");
            float rate = sequence.ConstantTransitionRate;
            if (sequence.Frames.Count != 170 || rate != 0.5f || !float.IsFinite(rate))
                throw new InvalidDataException("R&C1 class 1134 sequence 1 no longer matches the retail timing specimen.");

            var frames = new List<double[]>(sequence.Frames.Count);
            foreach (var frame in sequence.Frames)
            {
                var posed = Rac1MobyPose.PoseSingleJointRigid(cls.Mesh, cls.Joints, frame);
                var world = new double[posed.Length];
                for (int i = 0; i < posed.Length; i += 3)
                {
                    var (nx, ny, nz) = Rac1Instances.TransformMobyPoint(instance, posed[i], posed[i + 1], posed[i + 2]);
                    world[i] = R2(nx); world[i + 1] = R2(nz); world[i + 2] = R2(ny);
                    minX = System.Math.Min(minX, nx); minY = System.Math.Min(minY, nz); minZ = System.Math.Min(minZ, ny);
                    maxX = System.Math.Max(maxX, nx); maxY = System.Math.Max(maxY, nz); maxZ = System.Math.Max(maxZ, ny);
                }
                frames.Add(world);
            }
            var byTexture = new SortedDictionary<int, List<int>>();
            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                int textureId = cls.TriangleTextureIds[face];
                if (textureId < 0 || !textureIds.Contains(textureId))
                    throw new InvalidDataException($"R&C1 class 1134 references missing texture {textureId}.");
                if (!byTexture.TryGetValue(textureId, out var indices)) byTexture[textureId] = indices = [];
                indices.Add(cls.Mesh.Indices[face * 3]);
                indices.Add(cls.Mesh.Indices[face * 3 + 1]);
                indices.Add(cls.Mesh.Indices[face * 3 + 2]);
            }
            foreach (var (textureId, indices) in byTexture)
            {
                output.Add(new RuntimeAnimatedMesh(
                    Name: $"moby{instance.OClass}_i{instance.Index}_t{textureId}",
                    AssetKind: "moby", TextureId: textureId,
                    Uvs: cls.Mesh.Uvs, Indices: indices.ToArray(), Colors: [],
                    Frames: frames, FramesPerSecond: rate * NtscUpdateHz));
            }
            animatedInstanceIndices.Add(instance.Index);
        }
        return output;
    }

    private static void PlaceMobyInstances(
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
        HashSet<int> textureIds,
        HashSet<int> skippedInstanceIndices,
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
            if (skippedInstanceIndices.Contains(instance.Index)) continue;
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
