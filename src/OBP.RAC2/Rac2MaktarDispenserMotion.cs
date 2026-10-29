namespace OBP.RAC2;

/// <summary>
/// Diagnostic-only orientation and sampled presentation of the retail
/// class-2581 keys. Neither method changes the authored Moby placement,
/// original source frames, or the normal Maktar scene.
/// </summary>
public static class Rac2MaktarDispenserMotion
{
    public const int SubframesPerKey = 6;

    /// <summary>
    /// The six source yaw values all orient local +Y radially away from
    /// arena centre (500, 601). The inactive preview displayed an arm into
    /// the exterior wall. Face its extending axis into the combat floor for
    /// the isolated visual study. This is NOT a recovered native actor state.
    /// </summary>
    public static (double X, double Y, double Z) InwardPreviewRotation(
        (double X, double Y, double Z) nativeRotation) =>
        (nativeRotation.X, nativeRotation.Y, nativeRotation.Z + Math.PI);

    /// <summary>
    /// Smooth a native sequence of posed vertex arrays in either direction.
    /// A ping-pong loop prevents the sudden last-to-first snap of the old
    /// stepwise preview. Vertex-space blending approximates native joint
    /// interpolation and is restricted to the source-inspection mode.
    /// </summary>
    public static IReadOnlyList<double[]> SmoothExtendRetract(
        IReadOnlyList<double[]> nativeKeys, int subdivisions = SubframesPerKey)
    {
        ArgumentNullException.ThrowIfNull(nativeKeys);
        if (nativeKeys.Count < 2 || subdivisions is < 2 or > 16)
            throw new ArgumentException("Preview requires at least two keys and 2..16 subdivisions.");
        int length = nativeKeys[0].Length;
        if (length == 0 || length % 3 != 0 ||
            nativeKeys.Any(key => key is null || key.Length != length || key.Any(v => !double.IsFinite(v))))
            throw new InvalidDataException("Invalid Maktar source animation vertex data.");

        var result = new List<double[]>(2 * (nativeKeys.Count - 1) * subdivisions);
        void Segment(double[] start, double[] end)
        {
            for (int step = 0; step < subdivisions; step++)
            {
                double alpha = step / (double)subdivisions;
                var positions = new double[length];
                for (int i = 0; i < length; i++)
                    positions[i] = start[i] + (end[i] - start[i]) * alpha;
                result.Add(positions);
            }
        }

        for (int key = 0; key + 1 < nativeKeys.Count; key++)
            Segment(nativeKeys[key], nativeKeys[key + 1]);
        for (int key = nativeKeys.Count - 1; key > 0; key--)
            Segment(nativeKeys[key], nativeKeys[key - 1]);
        return result;
    }
}
