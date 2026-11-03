using OBP.Core.Math;

namespace OBP.Runtime.Camera;

/// <summary>Outcome of measuring animated-mesh bounds for presentation camera framing.</summary>
public enum AnimatedMeshBoundsStatus
{
    /// <summary>Finite, non-empty bounds were measured.</summary>
    Ok,
    /// <summary>The world has no animated meshes to frame.</summary>
    NoAnimatedMeshes,
    /// <summary>Animated meshes exist but none of the selected frames holds a vertex.</summary>
    NoUsableVertices,
    /// <summary>A selected frame's coordinate array is not a whole number of XYZ triples.</summary>
    MalformedFrame,
    /// <summary>A selected frame holds a NaN or infinite coordinate.</summary>
    NonFiniteCoordinate,
}

/// <summary>Bounds are present only when <see cref="Status"/> is <see cref="AnimatedMeshBoundsStatus.Ok"/>.</summary>
public readonly record struct AnimatedMeshBoundsResult(
    AnimatedMeshBoundsStatus Status,
    ObpBounds? Bounds,
    string Instance,
    int MeshCount,
    string Detail)
{
    public bool Succeeded => Status == AnimatedMeshBoundsStatus.Ok;
}

/// <summary>
/// Engine-neutral, presentation-only bounds measurement for animation-only camera framing.
/// It validates coordinates as supplied and never reinterprets native animation data; on any
/// defect it reports a status instead of producing bounds a camera could consume.
/// </summary>
public static class AnimatedMeshFraming
{
    /// <summary>
    /// Instance identity of an animated mesh: its name without the trailing <c>_t&lt;textureId&gt;</c>
    /// surface suffix. Compared by equality, so <c>m_i1</c> never selects <c>m_i10</c>.
    /// </summary>
    public static string InstanceKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int cut = name.LastIndexOf("_t", StringComparison.Ordinal);
        if (cut < 0 || cut + 2 >= name.Length)
            return name;
        // RAC3 names its materialless surface with texture id -1 (`_t-1`).
        int digits = name[cut + 2] == '-' ? cut + 3 : cut + 2;
        if (digits >= name.Length)
            return name;
        for (int i = digits; i < name.Length; i++)
            if (!char.IsAsciiDigit(name[i]))
                return name;
        return name[..cut];
    }

    /// <summary>Measure the bounds of every frame of the first mesh's instance.</summary>
    public static AnimatedMeshBoundsResult MeasureFirstInstance(IReadOnlyList<RuntimeAnimatedMesh>? meshes)
    {
        if (meshes is not { Count: > 0 })
            return Fail(AnimatedMeshBoundsStatus.NoAnimatedMeshes, "", 0, "no animated meshes");

        string instance = InstanceKey(meshes[0].Name);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        int meshCount = 0;
        bool any = false;

        foreach (var mesh in meshes)
        {
            if (!string.Equals(InstanceKey(mesh.Name), instance, StringComparison.Ordinal))
                continue;
            meshCount++;
            for (int f = 0; f < (mesh.Frames?.Count ?? 0); f++)
            {
                double[]? frame = mesh.Frames![f];
                if (frame is null || frame.Length == 0)
                    continue;
                if (frame.Length % 3 != 0)
                    return Fail(AnimatedMeshBoundsStatus.MalformedFrame, instance, meshCount,
                        $"{mesh.Name} frame {f} has {frame.Length} coordinates, not a multiple of 3");
                for (int i = 0; i < frame.Length; i += 3)
                {
                    double x = frame[i], y = frame[i + 1], z = frame[i + 2];
                    if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                        return Fail(AnimatedMeshBoundsStatus.NonFiniteCoordinate, instance, meshCount,
                            $"{mesh.Name} frame {f} vertex {i / 3} is not finite");
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                    any = true;
                }
            }
        }

        if (!any)
            return Fail(AnimatedMeshBoundsStatus.NoUsableVertices, instance, meshCount,
                $"instance {instance} has no frame vertices");

        // A single point or flat extent is valid; hosts apply their own minimum framing radius.
        return new AnimatedMeshBoundsResult(AnimatedMeshBoundsStatus.Ok,
            new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ)),
            instance, meshCount, "");
    }

    private static AnimatedMeshBoundsResult Fail(AnimatedMeshBoundsStatus status, string instance, int count, string detail) =>
        new(status, null, instance, count, detail);
}
