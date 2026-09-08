using OBP.PS2.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.RAC3;

public static partial class Rac3WorldImport
{
    private const int PreviewTable = 1;

    private sealed record PreviewSpec(int OClass, int Sequence, int[] InstanceIds);

    private static readonly PreviewSpec[] PreviewSpecs =
    [
        new(6800, 2, [665, 666, 667, 668]),
        new(6577, 2, [513, 514, 515]),
        new(6317, 4, [477, 478, 479]),
        new(6886, 15, Enumerable.Range(672, 28).ToArray()),
    ];

    /// <summary>
    /// OBP showcase admission, not recovered gameplay state: explicitly pinned
    /// authored Veldin instances are lifted onto the neutral frame-animation
    /// bridge only when every decoded frame remains inside its retail sphere.
    /// </summary>
    private static List<RuntimeAnimatedMesh> BuildAnimationPreview(
        int tableIndex,
        IReadOnlyList<UyaGameplay.MobyInstance> instances,
        IReadOnlyDictionary<int, UyaAssets.MobyVisualClass> classes,
        out HashSet<int> animatedInstances)
    {
        animatedInstances = [];
        var output = new List<RuntimeAnimatedMesh>();
        if (tableIndex != PreviewTable) return output;

        foreach (var spec in PreviewSpecs)
        {
            if (!classes.TryGetValue(spec.OClass, out var cls)) continue;
            if (!cls.Mesh.SkinStateFullyResolved || cls.Joints.Count <= 1) continue;
            var sequence = cls.Sequences.FirstOrDefault(s => s.Index == spec.Sequence);
            if (sequence is null || sequence.Frames.Count == 0 || sequence.BoundingSphere is null) continue;

            var classFrames = PoseAndValidateClassFrames(cls, sequence);
            if (classFrames is null) continue;

            float speed = sequence.Frames[0].Speed;
            float fps = (float)Math.Clamp((speed <= 0 ? 0.125 : speed) * 60.0, 1.0, 60.0);
            var surfaces = BuildAnimationSurfaces(cls);
            foreach (int instanceId in spec.InstanceIds)
            {
                var instance = instances.FirstOrDefault(i => i.OClass == spec.OClass && i.Index == instanceId);
                if (instance is null) continue;

                animatedInstances.Add(instance.Index);
                double[] transform = UyaGameplay.MobyTransform(instance);
                var worldFrames = classFrames.Select(frame => PlaceFrame(frame, transform)).ToArray();

                foreach (var surface in surfaces)
                {
                    output.Add(new RuntimeAnimatedMesh(
                        $"uya-preview-moby{spec.OClass}_i{instance.Index}_s{spec.Sequence}_t{surface.TextureId}",
                        "moby", surface.TextureId, cls.Mesh.Uvs, surface.Indices, [], worldFrames, fps));
                }
            }
        }
        return output;
    }

    private sealed record AnimationSurface(int TextureId, int[] Indices);

    private static AnimationSurface[] BuildAnimationSurfaces(UyaAssets.MobyVisualClass cls)
    {
        return Enumerable.Range(0, cls.TriangleTextureIds.Length)
            .GroupBy(face => cls.TriangleTextureIds[face])
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var indices = new int[group.Count() * 3];
                int at = 0;
                foreach (int face in group)
                {
                    indices[at++] = cls.Mesh.Indices[face * 3];
                    indices[at++] = cls.Mesh.Indices[face * 3 + 1];
                    indices[at++] = cls.Mesh.Indices[face * 3 + 2];
                }
                int textureId = group.Key == 0xff ? -1 : group.Key;
                return new AnimationSurface(textureId, indices);
            })
            .ToArray();
    }

    private static double[][]? PoseAndValidateClassFrames(
        UyaAssets.MobyVisualClass cls,
        GcUyaMoby.MobySequence sequence)
    {
        var sphere = sequence.BoundingSphere!.Value;
        double k = cls.Mesh.Scale / 1024.0;
        double cx = sphere.X * k, cy = sphere.Y * k, cz = sphere.Z * k;
        double radius = sphere.Radius * k;
        if (!double.IsFinite(cx) || !double.IsFinite(cy) || !double.IsFinite(cz) ||
            !double.IsFinite(radius) || radius <= 0) return null;

        var frames = new double[sequence.Frames.Count][];
        for (int f = 0; f < sequence.Frames.Count; f++)
        {
            var posed = GcUyaMobyPose.Pose(cls.Mesh, cls.Joints, sequence.Frames[f]);
            for (int i = 0; i < posed.Length; i += 3)
            {
                double dx = posed[i] - cx, dy = posed[i + 1] - cy, dz = posed[i + 2] - cz;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > radius * 1.001) return null;
            }
            frames[f] = posed;
        }
        return frames;
    }

    private static double[] PlaceFrame(double[] nativeZUp, double[] m)
    {
        var world = new double[nativeZUp.Length];
        for (int i = 0; i < nativeZUp.Length; i += 3)
        {
            double x = nativeZUp[i];
            double y = nativeZUp[i + 2];
            double z = nativeZUp[i + 1];
            world[i] = m[0] * x + m[4] * y + m[8] * z + m[12];
            world[i + 1] = m[1] * x + m[5] * y + m[9] * z + m[13];
            world[i + 2] = m[2] * x + m[6] * y + m[10] * z + m[14];
        }
        return world;
    }
}
