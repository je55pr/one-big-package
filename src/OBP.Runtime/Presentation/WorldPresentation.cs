using OBP.Core.Math;

namespace OBP.Runtime.Presentation;

/// <summary>
/// Resolves the engine-independent <see cref="PresentationState"/> for a level
/// from its <see cref="RuntimeEnvironment"/>, and the per-frame
/// <see cref="FogState"/> for a region the host has already resolved with
/// <see cref="EnvResolver"/>. This is the single home for the OBP fog/atmosphere
/// maths that used to be duplicated between <c>RuntimeWorldScene.ConfigureEnvironment</c>
/// and the host's per-frame hero-lighting update.
/// </summary>
public static class WorldPresentation
{
    /// <summary>Clear colour when a level defines neither a background nor a fog colour.</summary>
    public static readonly Rgb DefaultBackground = new(0.05, 0.06, 0.09);

    /// <summary>Linear depth interpolation is the closest simple Godot mapping to the recovered PS2 fog ramp.</summary>
    public const double FogCurve = 1.0;

    /// <summary>
    /// Keep world tone mapping neutral. Atmosphere work should not masquerade as
    /// recovered native post-processing; any future grade belongs to an explicit
    /// presentation option.
    /// </summary>
    public const ToneMapMode WorldToneMap = ToneMapMode.Linear;

    /// <summary>
    /// Resolve load-time atmosphere without inventing level-specific tuning.
    /// Native metadata is passed through directly; missing background/ambient
    /// values use explicit presentation fallbacks and carry that provenance.
    /// </summary>
    public static PresentationState Resolve(RuntimeEnvironment? env, ObpBounds bounds)
    {
        Rgb background;
        RuntimeAtmosphereSource backgroundSource;
        if (env?.BackgroundColour is { } bg)
        {
            background = Rgb.From(bg).Clamp01();
            backgroundSource = env.BackgroundSource;
        }
        else if (env?.FogColour is { } fc)
        {
            background = Rgb.From(fc).Clamp01();
            backgroundSource = env.FogSource;
        }
        else
        {
            background = DefaultBackground;
            backgroundSource = RuntimeAtmosphereSource.PresentationFallback;
        }

        Rgb ambient = env?.AmbientColour is { } a ? Rgb.From(a).Clamp01() : Rgb.White;
        RuntimeAtmosphereSource ambientSource = env?.AmbientColour is not null
            ? env.AmbientSource
            : RuntimeAtmosphereSource.PresentationFallback;
        var fog = ResolveFog(env, bounds);

        return new PresentationState(
            background,
            ambient,
            1.0,
            fog,
            ToneMap.Neutral,
            ColourGrade.Neutral,
            backgroundSource,
            ambientSource,
            fog.Enabled ? env!.FogSource : null);
    }

    /// <summary>
    /// Compatibility hook retained for callers that previously asked for a
    /// presentation tone map. Atmosphere recovery keeps it neutral.
    /// </summary>
    public static ToneMap ResolveToneMap(RuntimeEnvironment? env)
    {
        _ = env;
        return ToneMap.Neutral;
    }

    /// <summary>Atmosphere recovery does not apply a colour grade.</summary>
    public static ColourGrade ResolveGrade(RuntimeEnvironment? env)
    {
        _ = env;
        return ColourGrade.Neutral;
    }

    /// <summary>Load-time depth fog straight from the level settings / nearest fog sample.</summary>
    public static FogState ResolveFog(RuntimeEnvironment? env, ObpBounds bounds)
    {
        _ = bounds; // retained in the public contract; native fog planes are not stretched to fit OBP bounds.
        if (env?.FogColour is not { } fc
            || env.FogFarDistance <= env.FogNearDistance
            || env.FogFarDistance <= 0)
        {
            return FogState.Disabled;
        }

        return BuildFog(Rgb.From(fc), env.FogNearDistance, env.FogFarDistance, env.FogFarVisibility);
    }

    /// <summary>
    /// Per-frame depth fog for a region already resolved by <see cref="EnvResolver"/>.
    /// Godot cannot express the PS2's independent endpoint visibility exactly,
    /// so this simple mapping preserves the native colour and near/far planes and
    /// derives density only from the recovered far visibility.
    /// </summary>
    public static FogState FogFromResolved(EnvResolved r, double worldDiagonal)
    {
        _ = worldDiagonal;
        if (!r.HasFog || r.FogFar <= r.FogNear || r.FogFar <= 0)
        {
            return FogState.Disabled;
        }

        return BuildFog(r.FogColour, r.FogNear, r.FogFar, r.FogFarVisibility);
    }

    private static FogState BuildFog(Rgb colour, double near, double far, double farVisibility)
    {
        double begin = System.Math.Max(0.0, near);
        double end = System.Math.Max(begin + 0.001, far);
        double density = System.Math.Clamp(1.0 - farVisibility, 0.0, 1.0);
        return new FogState(true, colour.Clamp01(), begin, end, density, FogCurve);
    }
}
