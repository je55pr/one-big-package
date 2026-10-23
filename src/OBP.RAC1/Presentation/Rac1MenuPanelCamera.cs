using System.Numerics;

namespace OBP.RAC1.Presentation;

/// <summary>
/// The original pause scene resets D_00186F40 camera position to
/// (256, 256, 64), resets the basis to identity, then positions every
/// class-0x472 panel Moby at that camera position. Source relative poses
/// are therefore projected with the same reset basis. The view matrix
/// columns are -world Y (screen X), -world Z (screen Y), +world X (depth).
/// Source: FUN_00218d10, FUN_001f2260, FUN_001f2d98, FUN_00237a78.
/// </summary>
public static class Rac1MenuPanelCamera
{
    public static readonly Vector3 NativeCameraPosition = new(256f, 256f, 64f);
    public const float HorizontalTanHalfFov = 0.63f;
    public const float NtscVerticalFovFactor = 0.775f;
    public const float VerticalTanHalfFov = HorizontalTanHalfFov * NtscVerticalFovFactor;
    public const float NearPlane = 32f / 1024f;

    public static Vector2 ProjectRelative(Vector3 cameraRelativeVertex, Vector2 viewportSize)
    {
        if (!float.IsFinite(cameraRelativeVertex.X) || !float.IsFinite(cameraRelativeVertex.Y) ||
            !float.IsFinite(cameraRelativeVertex.Z) ||
            !float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) ||
            viewportSize.X <= 0 || viewportSize.Y <= 0 || cameraRelativeVertex.X <= NearPlane)
            throw new ArgumentOutOfRangeException(nameof(cameraRelativeVertex),
                "Native panel vertex is outside the positive-depth view frustum.");
        return new Vector2(
            viewportSize.X * .5f -
                cameraRelativeVertex.Y * viewportSize.X * .5f / (HorizontalTanHalfFov * cameraRelativeVertex.X),
            viewportSize.Y * .5f -
                cameraRelativeVertex.Z * viewportSize.Y * .5f / (VerticalTanHalfFov * cameraRelativeVertex.X));
    }

    /// <summary>
    /// FUN_002196b8 projects semantic frame points 0 and 3, transformed
    /// from the class's native attachment chains by FUN_0023a318. Unlike
    /// textured mesh extrema, these exclude the source art's outside fringe.
    /// </summary>
    public static Rac1MenuPanelProjection.Bounds ProjectPanel(
        Rac1MenuPanelMotion.PanelPose pose, Vector2 viewportSize)
    {
        if (MathF.Abs(pose.FirstCorner.X - pose.OppositeCorner.X) > .0001f)
            throw new InvalidDataException("Class 0x472's native attachment frame no longer lies in a camera-facing plane.");
        Vector2 topLeft = ProjectRelative(pose.FirstCorner, viewportSize);
        Vector2 bottomRight = ProjectRelative(pose.OppositeCorner, viewportSize);
        return new Rac1MenuPanelProjection.Bounds(topLeft.X, topLeft.Y,
            bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }
}
