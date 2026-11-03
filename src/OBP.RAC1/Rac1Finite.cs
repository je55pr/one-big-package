using System.Numerics;

namespace OBP.RAC1;

/// <summary>Shared finite-value guards for native-state admission checks.</summary>
internal static class Rac1Finite
{
    /// <summary>True when every component of <paramref name="v"/> is a finite float.</summary>
    internal static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
