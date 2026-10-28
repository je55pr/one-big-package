using Godot;

namespace OBP.Game;

/// <summary>Existing OBP contact geometry; these are scene queries, not recovered native collision formulas.</summary>
internal static class ActorContactGeometry
{
    internal static bool SegmentBoxFraction(Vector3 a, Vector3 b, Aabb bounds, out float fraction)
    {
        float near = 0, far = 1; Vector3 direction = b - a;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < .000001f)
            {
                if (a[axis] < bounds.Position[axis] || a[axis] > bounds.End[axis]) { fraction = 0; return false; }
                continue;
            }
            float t0 = (bounds.Position[axis] - a[axis]) / direction[axis], t1 = (bounds.End[axis] - a[axis]) / direction[axis];
            near = MathF.Max(near, MathF.Min(t0, t1)); far = MathF.Min(far, MathF.Max(t0, t1));
            if (near > far) { fraction = 0; return false; }
        }
        fraction = near; return true;
    }

    internal static bool WrenchSegmentIntersects(Aabb bounds, Vector3 from, Vector3 to)
    {
        float first = 0, last = 1;
        Vector3 delta = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            float low = bounds.Position[axis], high = bounds.End[axis];
            if (Mathf.Abs(delta[axis]) < 1e-7f)
            {
                if (from[axis] < low || from[axis] > high) return false;
                continue;
            }
            float a = (low - from[axis]) / delta[axis], b = (high - from[axis]) / delta[axis];
            first = Math.Max(first, Math.Min(a, b)); last = Math.Min(last, Math.Max(a, b));
            if (first > last) return false;
        }
        return true;
    }
}
