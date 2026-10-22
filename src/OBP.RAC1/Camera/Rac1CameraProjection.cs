namespace OBP.RAC1.Camera;

/// <summary>SCUS-97199 gameplay projection, screen_setup 0x001f7848 / projection_setup 0x001f7980.</summary>
public static class Rac1CameraProjection
{
    public const double HorizontalHalfAngleTangent = 0.6299999952316284d;
    public const double NtscVerticalScale = 0.7749999761581421d;
    public static double VerticalFieldOfViewRadians =>
        2d * Math.Atan(HorizontalHalfAngleTangent * NtscVerticalScale);
}
