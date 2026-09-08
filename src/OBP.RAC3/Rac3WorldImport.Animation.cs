using OBP.PS2.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.RAC3;

public static partial class Rac3WorldImport
{
    private const int PreviewTable = 1;
    private const int PreviewOClass = 6800;
    private const int PreviewInstance = 665;
    private const int PreviewSequence = 2;

    /// <summary>
    /// OBP showcase admission, not recovered gameplay state: one authored Veldin
    /// instance is lifted onto the neutral frame-animation bridge using a retail
    /// sequence whose every decoded frame remains inside its authored sphere.
    /// </summary>
    private static List<RuntimeAnimatedMesh> BuildAnimationPreview(
        int tableIndex,
        IReadOnlyList<UyaGameplay.MobyInstance> instances,
        IReadOnlyDictionary<int, UyaAssets.MobyVisualClass> classes,
        out HashSet<int> animatedInstances)
    {
        animatedInstances = [];
        var output = new List<RuntimeAnimatedMesh>();
        if (tableIndex != PreviewTable || !classes.TryGetValue(PreviewOClass, out var cls)) return output;
        if (!cls.Mesh.SkinStateFullyResolved || cls.Joints.Count <= 1) return output;
        var sequence = cls.Sequences.FirstOrDefault(s => s.Index == PreviewSequence);
        if (sequence is null || sequence.Frames.Count == 0 || sequence.BoundingSphere is null) return output;

        var classFrames = PoseAndValidateClassFrames(cls, sequence);
        if (classFrames is null) return output;

        var instance = instances.FirstOrDefault(i =>
            i.OClass == PreviewOClass && i.Index == PreviewInstance);
        if (instance is null) return output;
        animatedInstances.Add(instance.Index);

        double[] transform = UyaGameplay.MobyTransform(instance);
        var worldFrames = classFrames.Select(frame => PlaceFrame(frame, transform)).ToArray();
        float speed = sequence.Frames[0].Speed;
        float fps = (float)Math.Clamp((speed <= 0 ? 0.125 : speed) * 60.0, 1.0, 60.0);

        foreach (var group in Enumerable.Range(0, cls.TriangleTextureIds.Length)
                     .GroupBy(face => cls.TriangleTextureIds[face]).OrderBy(group => group.Key))
        {
            var indices = new List<int>();
            foreach (int face in group)
            {
                indices.Add(cls.Mesh.Indices[face * 3]);
                indices.Add(cls.Mesh.Indices[face * 3 + 1]);
                indices.Add(cls.Mesh.Indices[face * 3 + 2]);
            }
            int textureId = group.Key == 0xff ? -1 : group.Key;
            output.Add(new RuntimeAnimatedMesh(
                $"uya-preview-moby{PreviewOClass}_i{instance.Index}_s{PreviewSequence}_t{textureId}",
                "moby", textureId, cls.Mesh.Uvs, indices.ToArray(), [], worldFrames, fps));
        }
        return output;
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
