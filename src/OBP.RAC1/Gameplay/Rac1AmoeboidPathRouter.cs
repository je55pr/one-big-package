using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Level;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The exact 0x276fe8 path-visibility/mask-expansion route algorithm, with
/// segment-obstruction tests supplied by the world collision host.
/// The Godot probe is an adapter, not the original PS2 polygon solver.
/// </summary>
public sealed class Rac1AmoeboidPathRouter
{
    public sealed record NativePath(Vector3[] Points, uint[] ConnectionMasks);

    private readonly IReadOnlyList<NativePath> _paths;

    public Rac1AmoeboidPathRouter(IReadOnlyList<NativePath> paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        foreach (var path in _paths)
        {
            if (path.Points.Length != path.ConnectionMasks.Length ||
                path.Points.Length < 1 ||
                path.Points.Any(point => !Finite(point)))
                throw new InvalidDataException("Amoeboid native path points/masks are invalid.");
        }
    }

    public int Count => _paths.Count;
    public int PointCount(int pathIndex) => _paths[pathIndex].Points.Length;

    public static Rac1AmoeboidPathRouter Read(byte[] gameplay)
    {
        var points = Rac1Paths.Read(gameplay);
        int pointer = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(0x70));
        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(pointer + 4));
        var paths = new NativePath[points.Count];
        for (int index = 0; index < paths.Length; index++)
        {
            // Other game systems may use long paths, but the native source
            // masks are 32-bit and Amoeboid-authored routes never exceed 4.
            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                gameplay.AsSpan(pointer + 0x10 + index * 4));
            int head = checked(pointer + dataOffset + offset + 0x10);
            var members = points[index];
            var masks = new uint[members.Length];
            for (int i = 0; i < members.Length; i++)
                masks[i] = BinaryPrimitives.ReadUInt32LittleEndian(
                    gameplay.AsSpan(head + i * 0x10 + 0x0c));
            paths[index] = new(members, masks);
        }
        return new(paths);
    }

    /// <summary>
    /// Returns native 0x276fe8's success and waypoint. A successful direct
    /// route returns the original target rather than an artificial vertex.
    /// </summary>
    public bool TryFollowPath(int obstacleRegion, int pathIndex, float radius,
        Vector3 start, Vector3 target,
        Func<int, Vector3, Vector3, bool> segmentIntersects,
        out Vector3 waypoint)
    {
        if ((uint)pathIndex >= _paths.Count)
            throw new ArgumentOutOfRangeException(nameof(pathIndex));
        if (obstacleRegion < 0 || !float.IsFinite(radius) || radius < 0 ||
            !Finite(start) || !Finite(target))
            throw new ArgumentOutOfRangeException(nameof(radius));
        ArgumentNullException.ThrowIfNull(segmentIntersects);
        waypoint = default;
        if (!Blocked(obstacleRegion, start, target, radius, segmentIntersects))
        {
            waypoint = target;
            return true;
        }
        NativePath path = _paths[pathIndex];
        if (path.Points.Length > 32)
            throw new InvalidDataException("Native bitmask paths support at most 32 vertices.");
        uint from = VisibleMask(obstacleRegion, path.Points, start, radius, segmentIntersects);
        uint to = VisibleMask(obstacleRegion, path.Points, target, radius, segmentIntersects);
        uint reachable = ExpandConnections(from, to, path.ConnectionMasks);
        if (reachable == 0) return false;
        float distance = 10000f;
        int best = -1;
        for (int i = 0; i < path.Points.Length; i++)
        {
            if ((reachable & (1u << i)) == 0) continue;
            float current = Vector3.Distance(path.Points[i], target);
            if (current < distance)
            {
                distance = current;
                best = i;
            }
        }
        if (best < 0) return false;
        waypoint = path.Points[best];
        return true;
    }

    /// <summary>Native 0x276c40 expands destination connectivity up to 32 steps.</summary>
    public static uint ExpandConnections(uint from, uint to, IReadOnlyList<uint> adjacency)
    {
        if (adjacency.Count > 32) throw new ArgumentOutOfRangeException(nameof(adjacency));
        for (int depth = 0; depth < 32; depth++)
        {
            if ((from & to) != 0) return from & to;
            uint expanded = to;
            for (int i = 0; i < adjacency.Count; i++)
                if ((to & (1u << i)) != 0) expanded |= adjacency[i];
            to = expanded;
        }
        return 0;
    }

    private static uint VisibleMask(int region, IReadOnlyList<Vector3> points, Vector3 origin,
        float radius, Func<int, Vector3, Vector3, bool> intersects)
    {
        uint mask = 0;
        for (int i = 0; i < points.Count && i < 32; i++)
            if (!Blocked(region, origin, points[i], radius, intersects))
                mask |= 1u << i;
        return mask;
    }

    private static bool Blocked(int region, Vector3 from, Vector3 to, float radius,
        Func<int, Vector3, Vector3, bool> intersects)
    {
        if (radius == 0f) return intersects(region, from, to);
        // Source 0x276fe8 tests the two sideways clearance traces.
        Vector2 forward = new(from.X - to.X, from.Y - to.Y);
        if (forward.LengthSquared() < 1e-12f)
            return intersects(region, from, to);
        forward = Vector2.Normalize(forward) * radius;
        var offset = new Vector3(forward.Y, -forward.X, 0f);
        return intersects(region, from - offset, to) ||
               intersects(region, from + offset, to);
    }

    private static bool Finite(Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
