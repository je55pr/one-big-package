using System.Numerics;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Engine-neutral R&C1 FUN_00237a78 projection of two opposite class-0x472
/// frame corners. The caller supplies the retail view transform as a pure
/// projection callback; this routine never fabricates a camera or layout.
/// </summary>
public static class Rac1MenuPanelProjection
{
    public readonly record struct Bounds(float X, float Y, float Width, float Height)
    {
        public bool HasArea => Width > 0 && Height > 0;
    }

    public readonly record struct Target(int Width, int Height, int Log2Width, int Log2Height);

    public static Bounds ProjectFloat(
        Vector3 first, Vector3 opposite, Vector3 cameraPosition,
        Func<Vector4, Vector4> nativeViewTransform,
        Vector2 screenScale, Vector2 halfScreenSize)
    {
        ArgumentNullException.ThrowIfNull(nativeViewTransform);
        if (!Vector3IsFinite(first) || !Vector3IsFinite(opposite) ||
            !Vector3IsFinite(cameraPosition) || !Vector2IsFinite(screenScale) ||
            !Vector2IsFinite(halfScreenSize))
            throw new ArgumentOutOfRangeException(nameof(first), "Non-finite native projection inputs.");
        var a = nativeViewTransform(new Vector4((first - cameraPosition) * 1024f, 1f));
        var b = nativeViewTransform(new Vector4((opposite - cameraPosition) * 1024f, 1f));
        if (!float.IsFinite(a.W) || !float.IsFinite(b.W) ||
            MathF.Abs(a.W) < 1e-8 || MathF.Abs(b.W) < 1e-8)
            throw new InvalidDataException("Native R&C1 menu corner is outside projection range.");
        float x0 = (a.X / a.W) * screenScale.X * .25f;
        float y0 = (a.Y / a.W) * screenScale.Y * .25f;
        float x1 = (b.X / b.W) * screenScale.X * .25f;
        float y1 = (b.Y / b.W) * screenScale.Y * .25f;
        if (!float.IsFinite(x0) || !float.IsFinite(y0) ||
            !float.IsFinite(x1) || !float.IsFinite(y1))
            throw new InvalidDataException("Native menu screen projection overflowed.");
        return new(x0 + halfScreenSize.X, y0 + halfScreenSize.Y, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Native FUN_002196b8 texture target selection. Destination dimensions
    /// are rounded up to at least 128 and then to a power of two, with the
    /// PS2's 2^18 texel budget reducing target height if necessary.
    /// </summary>
    public static Target OffscreenTarget(int projectedWidth, int projectedHeight)
    {
        if (projectedWidth <= 0 || projectedHeight <= 0 ||
            projectedWidth > 16384 || projectedHeight > 16384)
            throw new ArgumentOutOfRangeException(nameof(projectedWidth));
        int widthLog = 7, heightLog = 7;
        while ((1 << widthLog) < projectedWidth) widthLog++;
        while ((1 << heightLog) < projectedHeight) heightLog++;
        while (widthLog + heightLog >= 18 && heightLog > 0) heightLog--;
        return new(1 << widthLog, 1 << heightLog, widthLog, heightLog);
    }

    private static bool Vector3IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Vector2IsFinite(Vector2 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y);
}
