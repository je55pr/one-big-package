using OBP.RAC1.Animation;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static void AddRatchetStandingLoop(
        Rac1LevelCore.Core core,
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
        HashSet<int> textureIds,
        List<RuntimeAnimatedMesh> output,
        HashSet<int> animatedInstanceIndices,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ)
    {
        const int RatchetClass = 0;
        const int StandingSequence = 0;
        const int BindAnchorSequence = 122;
        const float NtscUpdateHz = 60f;

        if (!classes.TryGetValue(RatchetClass, out var cls))
            throw new InvalidDataException("R&C1 Ratchet class 0 is missing.");
        var placements = instances.Where(i => i.OClass == RatchetClass).ToArray();
        if (placements.Length != 1 || placements[0].Index != 0)
            throw new InvalidDataException("R&C1 Ratchet is no longer the single class-0 placement at instance 0.");
        var instance = placements[0];

        var sequences = Rac1MobyAnimation.ReadRatchetSequences(
            core.Assets, core.Index, core.Header.RatchetSequencesOffset, cls.JointCount);
        var bind = sequences[BindAnchorSequence].Value
            ?? throw new InvalidDataException("R&C1 Ratchet bind-anchor sequence 122 is absent.");
        var standing = sequences[StandingSequence].Value
            ?? throw new InvalidDataException("R&C1 Ratchet standing sequence 0 is absent.");

        if (bind.Frames.Count != 21 || bind.ConstantTransitionRate != 0.5f ||
            !Rac1MobyPose.CanPoseRatchetHierarchy(cls.Mesh, cls.Joints, bind.Frames[0]) ||
            !Rac1MobyPose.IsRatchetHierarchyBindLinearAnchor(cls.Joints, bind.Frames[0]))
        {
            throw new InvalidDataException("R&C1 Ratchet sequence 122 no longer matches the pinned bind anchor.");
        }
        if (standing.Frames.Count != 10 ||
            standing.ConstantTransitionRateRaw != 0x3e000000u ||
            standing.ConstantTransitionRate != 0.125f ||
            standing.Frames.Any(frame => !Rac1MobyPose.CanPoseRatchetHierarchy(cls.Mesh, cls.Joints, frame)))
        {
            throw new InvalidDataException("R&C1 Ratchet sequence 0 no longer matches the pinned standing loop.");
        }

        var frames = new List<double[]>(standing.Frames.Count);
        foreach (var frame in standing.Frames)
        {
            var posed = Rac1MobyPose.PoseRatchetHierarchy(cls.Mesh, cls.Joints, frame);
            var world = new double[posed.Length];
            for (int i = 0; i < posed.Length; i += 3)
            {
                var point = Rac1Instances.TransformMobyPoint(
                    instance, posed[i], posed[i + 1], posed[i + 2]);
                world[i] = R2(point.X);
                world[i + 1] = R2(point.Z);
                world[i + 2] = R2(point.Y);
                minX = System.Math.Min(minX, point.X);
                minY = System.Math.Min(minY, point.Z);
                minZ = System.Math.Min(minZ, point.Y);
                maxX = System.Math.Max(maxX, point.X);
                maxY = System.Math.Max(maxY, point.Z);
                maxZ = System.Math.Max(maxZ, point.Y);
            }
            frames.Add(world);
        }
        var byTexture = new SortedDictionary<int, List<int>>();
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            int textureId = cls.TriangleTextureIds[face];
            if (textureId < 0 || !textureIds.Contains(textureId))
                throw new InvalidDataException($"R&C1 Ratchet references missing texture {textureId}.");
            if (!byTexture.TryGetValue(textureId, out var indices))
                byTexture[textureId] = indices = [];
            indices.Add(cls.Mesh.Indices[face * 3]);
            indices.Add(cls.Mesh.Indices[face * 3 + 1]);
            indices.Add(cls.Mesh.Indices[face * 3 + 2]);
        }

        foreach (var pair in byTexture)
        {
            output.Add(new RuntimeAnimatedMesh(
                Name: $"ratchet_i{instance.Index}_t{pair.Key}",
                AssetKind: "moby",
                TextureId: pair.Key,
                Uvs: cls.Mesh.Uvs,
                Indices: pair.Value.ToArray(),
                Colors: [],
                Frames: frames,
                FramesPerSecond: standing.ConstantTransitionRate * NtscUpdateHz));
        }
        animatedInstanceIndices.Add(instance.Index);
    }

    private static List<RuntimeAnimatedMesh> BuildAnimatedMobyMeshes(
        Rac1LevelCore.Core core,
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
        HashSet<int> textureIds,
        out HashSet<int> animatedInstanceIndices,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ)
    {
        animatedInstanceIndices = [];
        var output = new List<RuntimeAnimatedMesh>();

        AddRatchetStandingLoop(
            core, instances, classes, textureIds, output, animatedInstanceIndices,
            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);

        return output;
    }

    private static IReadOnlyList<RuntimeDynamicObject> BuildMobyDynamicObjects(
        IReadOnlyList<Rac1Instances.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac1StaticClasses.MobyClass> classes,
        HashSet<int> textureIds,
        HashSet<int> animatedInstanceIndices,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ)
    {
        var models = new Dictionary<int, IReadOnlyList<RuntimeObjectMesh>>();
        var animationSets = new Dictionary<int, RuntimeObjectAnimationSet>();
        foreach (int oClass in instances.Select(i => i.OClass).Distinct().Order())
        {
            if (!classes.TryGetValue(oClass, out var cls) || cls.Mesh.Indices.Length == 0) continue;
            var mesh = cls.Mesh;
            if (mesh.Indices.Length != cls.TriangleTextureIds.Length * 3 ||
                mesh.Uvs.Length != mesh.Positions.Length / 3 * 2)
                throw new InvalidDataException($"R&C1 moby class {oClass} has inconsistent surface arrays.");

            var byTexture = new SortedDictionary<int, List<int>>();
            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                int textureId = cls.TriangleTextureIds[face];
                if (textureId >= 0 && !textureIds.Contains(textureId))
                    throw new InvalidDataException($"R&C1 moby class {oClass} references missing texture {textureId}.");
                if (!byTexture.TryGetValue(textureId, out var indices)) byTexture[textureId] = indices = [];
                indices.Add(mesh.Indices[face * 3]);
                indices.Add(mesh.Indices[face * 3 + 1]);
                indices.Add(mesh.Indices[face * 3 + 2]);
            }

            var surfaces = new List<RuntimeObjectMesh>();
            foreach (var (textureId, sourceIndices) in byTexture)
            {
                var remap = new Dictionary<int, int>();
                var positions = new List<double>();
                var uvs = new List<float>();
                var indices = new List<int>(sourceIndices.Count);
                foreach (int sourceVertex in sourceIndices)
                {
                    if (!remap.TryGetValue(sourceVertex, out int outputVertex))
                    {
                        outputVertex = positions.Count / 3;
                        remap[sourceVertex] = outputVertex;
                        positions.Add(mesh.Positions[sourceVertex * 3]);
                        positions.Add(mesh.Positions[sourceVertex * 3 + 2]);
                        positions.Add(mesh.Positions[sourceVertex * 3 + 1]);
                        uvs.Add(mesh.Uvs[sourceVertex * 2]);
                        uvs.Add(mesh.Uvs[sourceVertex * 2 + 1]);
                    }
                    indices.Add(outputVertex);
                }
                surfaces.Add(new RuntimeObjectMesh(
                    "moby", textureId, positions.ToArray(), uvs.ToArray(), indices.ToArray()));
            }
            models[oClass] = surfaces;
            var animations = Rac1MobyAnimationProvider.BuildAdmittedAnimationSet(cls, surfaces);
            if (animations is not null) animationSets[oClass] = animations;
        }

        var output = new List<RuntimeDynamicObject>(instances.Count);
        foreach (var instance in instances)
        {
            IReadOnlyList<RuntimeObjectMesh> objectMeshes =
                !animatedInstanceIndices.Contains(instance.Index) && models.TryGetValue(instance.OClass, out var model)
                    ? model
                    : Array.Empty<RuntimeObjectMesh>();

            if (objectMeshes.Count > 0 && classes.TryGetValue(instance.OClass, out var cls))
            {
                for (int i = 0; i < cls.Mesh.Positions.Length; i += 3)
                {
                    var (nx, ny, nz) = Rac1Instances.TransformMobyPoint(
                        instance, cls.Mesh.Positions[i], cls.Mesh.Positions[i + 1], cls.Mesh.Positions[i + 2]);
                    minX = System.Math.Min(minX, nx); minY = System.Math.Min(minY, nz); minZ = System.Math.Min(minZ, ny);
                    maxX = System.Math.Max(maxX, nx); maxY = System.Math.Max(maxY, nz); maxZ = System.Math.Max(maxZ, ny);
                }
            }

            var nativePayloads = new List<RuntimeOpaquePayload>
            {
                new("rac1-moby-instance-0x78", instance.RawRecord),
            };
            if (instance.PVar is not null) nativePayloads.Add(new("rac1-pvar", instance.PVar));

            output.Add(new RuntimeDynamicObject(
                "rac1", instance.OClass, instance.Index, instance.Uid, $"moby:{instance.OClass}",
                $"moby:{instance.Index}", new RuntimeObjectTransform(Rac1Instances.MobyTransform(instance)), objectMeshes,
                nativePayloads, animationSets.GetValueOrDefault(instance.OClass)));
        }
        return output;
    }

}
