using OBP.PS2.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

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
        IReadOnlyList<Rac3Gameplay.MobyInstance> instances,
        IReadOnlyDictionary<int, Rac3Assets.MobyVisualClass> classes,
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
                double[] transform = Rac3Gameplay.MobyTransform(instance);
                var worldFrames = classFrames.Select(frame => PlaceFrame(frame, transform)).ToArray();

                foreach (var surface in surfaces)
                {
                    output.Add(new RuntimeAnimatedMesh(
                        $"uya-preview-moby{spec.OClass}_i{instance.Index}_s{spec.Sequence}_t{surface.TextureId}",
                        "moby", surface.TextureId, cls.Mesh.Uvs, surface.Indices, [], worldFrames, fps,
                        MaterialPresentation: surface.MaterialPresentation));
                }
            }
        }
        return output;
    }

    private sealed record AnimationSurface(int TextureId, int[] Indices, RuntimeMaterialPresentation? MaterialPresentation);

    private static AnimationSurface[] BuildAnimationSurfaces(Rac3Assets.MobyVisualClass cls)
    {
        var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation), List<int>>();
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            int textureId = cls.TriangleTextureIds[face] == 0xff ? -1 : cls.TriangleTextureIds[face];
            var presentation = PresentationFor(
                cls.Mesh.Materials, cls.Mesh.TriangleMaterialStateIndices, null, face,
                classifyMobySurface: true);
            var surface = (textureId, presentation);
            if (!bySurface.TryGetValue(surface, out var indices))
            {
                bySurface[surface] = indices = [];
            }

            indices.Add(cls.Mesh.Indices[face * 3]);
            indices.Add(cls.Mesh.Indices[face * 3 + 1]);
            indices.Add(cls.Mesh.Indices[face * 3 + 2]);
        }

        return bySurface
            .OrderBy(pair => pair.Key.TextureId)
            .ThenBy(pair => pair.Key.Presentation?.ToString(), StringComparer.Ordinal)
            .Select(pair => new AnimationSurface(
                pair.Key.TextureId, pair.Value.ToArray(), pair.Key.Presentation))
            .ToArray();
    }

    private static double[][]? PoseAndValidateClassFrames(
        Rac3Assets.MobyVisualClass cls,
        GcUyaMoby.MobySequence sequence)
    {
        var sphere = sequence.BoundingSphere!.Value;
        double k = cls.Mesh.Scale / 1024.0;
        double cx = sphere.X * k, cy = sphere.Y * k, cz = sphere.Z * k;
        double radius = sphere.Radius * k;
        // Source sequence spheres are approximate. In Veldin class 6317,
        // validated native frame vertices reach 1.086x the recorded radius.
        // Keep the original strict gate for legacy poses and a bounded 10%
        // margin for full native poses, rather than losing that preview.
        double radiusTolerance = cls.NativePoseSource is null ? 1.001 : 1.10;
        if (!double.IsFinite(cx) || !double.IsFinite(cy) || !double.IsFinite(cz) ||
            !double.IsFinite(radius) || radius <= 0) return null;

        var frames = new double[sequence.Frames.Count][];
        for (int f = 0; f < sequence.Frames.Count; f++)
        {
            double[] posed;
            try
            {
                // A natively posed mesh must keep its source inverse bind and
                // optional frame scale/translation channels during playback.
                posed = cls.NativePoseSource is { } source
                    ? Ps2MobyNativePose.Pose(source, cls.Mesh, sequence.Frames[f])
                    : GcUyaMobyPose.Pose(cls.Mesh, cls.Joints, sequence.Frames[f]);
            }
            catch (InvalidDataException)
            {
                // The source actor remains visible in its static first pose
                // when an unsupported preview animation frame is encountered.
                return null;
            }
            for (int i = 0; i < posed.Length; i += 3)
            {
                double dx = posed[i] - cx, dy = posed[i + 1] - cy, dz = posed[i + 2] - cz;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > radius * radiusTolerance) return null;
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
