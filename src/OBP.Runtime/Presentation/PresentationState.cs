using OBP.Core.Math;

namespace OBP.Runtime.Presentation;

/// <summary>
/// A linear RGB triple in the 0..1 range, engine-independent. The presentation
/// layer does all of its colour maths on this so the calculations can be unit
/// tested without a Godot <c>Color</c>.
/// </summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public static readonly Rgb White = new(1, 1, 1);

    /// <summary>Rec. 709 relative luminance.</summary>
    public double Luminance => (0.2126 * R) + (0.7152 * G) + (0.0722 * B);

    public Rgb Lerp(Rgb other, double t) =>
        new(R + ((other.R - R) * t), G + ((other.G - G) * t), B + ((other.B - B) * t));

    public Rgb Clamp01() => new(
        System.Math.Clamp(R, 0, 1),
        System.Math.Clamp(G, 0, 1),
        System.Math.Clamp(B, 0, 1));

    public static Rgb From((double R, double G, double B) c) => new(c.R, c.G, c.B);
}

/// <summary>How the viewport maps linear HDR radiance to display.</summary>
public enum ToneMapMode
{
    Linear,
    Reinhard,
    Filmic,
    Agx,
}

/// <summary>Tone-mapping selection for the world viewport.</summary>
public readonly record struct ToneMap(ToneMapMode Mode, double Exposure, double White)
{
    /// <summary>Matches Godot's default environment (no tone-map, unit exposure).</summary>
    public static readonly ToneMap Neutral = new(ToneMapMode.Linear, 1.0, 1.0);
}

/// <summary>
/// Resolved depth-fog parameters in world units. <see cref="Disabled"/> means the
/// level defines no usable fog and the host should turn fog off.
/// </summary>
public sealed record FogState(
    bool Enabled,
    Rgb Colour,
    double Begin,
    double End,
    double Density,
    double Curve)
{
    public static readonly FogState Disabled = new(false, default, 0, 0, 0, 0);
}

/// <summary>
/// The whole-level presentation description, resolved once from a
/// <see cref="RuntimeEnvironment"/> when a world loads. Everything here is
/// engine-independent; the Godot host translates it to an <c>Environment</c>.
/// </summary>
public sealed record PresentationState(
    Rgb Background,
    Rgb Ambient,
    double AmbientEnergy,
    FogState Fog,
    ToneMap ToneMap);

/// <summary>
/// Per-region lighting / fog resolved at one world point (the player or camera),
/// from <see cref="RuntimeLighting"/>. Queried every frame by the host as the
/// view moves. Colours are linear; <see cref="HeroTravel"/> is a unit direction
/// in OBP space describing the way the key light travels.
/// </summary>
public readonly record struct EnvResolved(
    Rgb Ambient,
    bool HasHeroLight,
    Vec3 HeroTravel,
    Rgb HeroColour,
    bool HasFog,
    Rgb FogColour,
    double FogNear,
    double FogFar,
    double FogFarVisibility);
