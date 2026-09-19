namespace OBP.Godot.Controls;

/// <summary>
/// Unconditioned SDL-standardized stick axes for one Godot joypad sample.
/// No dead zone, response curve, magnitude clamp or normalization is applied here.
/// </summary>
public readonly record struct RawGamepadAxes(
    float LeftX,
    float LeftY,
    float RightX,
    float RightY)
{
    public float LeftMagnitude => MathF.Sqrt((LeftX * LeftX) + (LeftY * LeftY));
    public float RightMagnitude => MathF.Sqrt((RightX * RightX) + (RightY * RightY));
}

/// <summary>Small pure helper mirroring how opposite raw action strengths form a signed axis.</summary>
public static class RawGamepadInputMath
{
    public static float ComposeAxis(float negativeStrength, float positiveStrength) =>
        positiveStrength - negativeStrength;
}

/// <summary>
/// Deterministic controller diagnostic payload. Name/GUID come from Godot's SDL-backed joypad API.
/// </summary>
public readonly record struct RawGamepadDiagnostic(
    int DeviceId,
    string Name,
    string Guid,
    bool MappingKnown,
    RawGamepadAxes Axes)
{
    public string Format()
    {
        string name = string.IsNullOrWhiteSpace(Name) ? "<unknown>" : Name;
        string guid = string.IsNullOrWhiteSpace(Guid) ? "<unavailable>" : Guid;
        return FormattableString.Invariant(
            $"device={DeviceId} name=\"{name}\" guid=\"{guid}\" known={MappingKnown.ToString().ToLowerInvariant()} left=({Axes.LeftX:0.000000},{Axes.LeftY:0.000000}) |left|={Axes.LeftMagnitude:0.000000} right=({Axes.RightX:0.000000},{Axes.RightY:0.000000}) |right|={Axes.RightMagnitude:0.000000}");
    }
}
