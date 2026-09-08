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

    /// <summary>Depth-fog ease-in curve — near geometry stays clear, the wall builds with distance.</summary>
    public const double FogCurve = 1.6;

    /// <summary>
    /// Tone-mapper for the world viewport. AgX rolls bright texels and sky off
    /// smoothly instead of the hard clip a linear map gives, which is the single
    /// biggest "pleasant" win over the historical default. Change here to retune
    /// the whole trilogy at once.
    /// </summary>
    public const ToneMapMode WorldToneMap = ToneMapMode.Agx;

    /// <summary>
    /// The load-time presentation for a world. <paramref name="bounds"/> is the
    /// full world bounds; its diagonal stretches the fog end-plane past the level
    /// so distant scenery still reads through heavy fog.
    /// </summary>
    public static PresentationState Resolve(RuntimeEnvironment? env, ObpBounds bounds)
    {
        Rgb background =
            env?.BackgroundColour is { } bg ? Rgb.From(bg)
            : env?.FogColour is { } fc ? Rgb.From(fc)
            : DefaultBackground;

        // The nearest env sample's baked "hero" colour is the ambient the game
        // lights the player with; lift it toward white so unlit (unshaded) world
        // geometry keeps its decoded colour instead of being crushed by a dim
        // ambient. Mirrors the historical ConfigureEnvironment behaviour.
        Rgb ambient = env?.AmbientColour is { } a
            ? new Rgb(0.55 + (0.45 * a.R), 0.55 + (0.45 * a.G), 0.55 + (0.45 * a.B))
            : Rgb.White;

        return new PresentationState(
            background,
            ambient,
            1.0,
            ResolveFog(env, bounds),
            ResolveToneMap(env),
            ResolveGrade(env));
    }

    /// <summary>
    /// AgX tone-map plus a small exposure nudge from the level's baked ambient:
    /// dark planets open up a little, bright ones pull back, so worlds read at a
    /// more consistent brightness without touching the decoded colours.
    /// </summary>
    public static ToneMap ResolveToneMap(RuntimeEnvironment? env)
    {
        double exposure = 1.0;
        if (env?.AmbientColour is { } a)
        {
            double ambLum = System.Math.Clamp(new Rgb(a.R, a.G, a.B).Luminance, 0.0, 1.0);
            exposure = System.Math.Clamp(1.0 + ((0.32 - ambLum) * 0.5), 0.9, 1.15);
        }

        return new ToneMap(WorldToneMap, exposure, 1.0);
    }

    /// <summary>
    /// A fixed, gentle post-tone-map lift — the unshaded PS2 palette is a touch
    /// flat, so a small contrast + saturation bump helps it read. Kept subtle and
    /// level-independent so it never fights the decoded look.
    /// </summary>
    public static ColourGrade ResolveGrade(RuntimeEnvironment? env)
    {
        _ = env;
        return new ColourGrade(1.0, 1.04, 1.05);
    }

    /// <summary>Load-time depth fog straight from the level settings / nearest fog sample.</summary>
    public static FogState ResolveFog(RuntimeEnvironment? env, ObpBounds bounds)
    {
        if (env?.FogColour is not { } fc
            || env.FogFarDistance <= env.FogNearDistance
            || env.FogFarDistance <= 0)
        {
            return FogState.Disabled;
        }

        return BuildFog(Rgb.From(fc), env.FogNearDistance, env.FogFarDistance, env.FogFarVisibility, bounds.Diagonal);
    }

    /// <summary>
    /// Per-frame depth fog for a region already resolved by <see cref="EnvResolver"/>.
    /// Same maths as <see cref="ResolveFog"/>; the host applies begin/end/density
    /// and leaves the curve as the load-time value.
    /// </summary>
    public static FogState FogFromResolved(EnvResolved r, double worldDiagonal)
    {
        if (!r.HasFog || r.FogFar <= r.FogNear || r.FogFar <= 0)
        {
            return FogState.Disabled;
        }

        return BuildFog(r.FogColour, r.FogNear, r.FogFar, r.FogFarVisibility, worldDiagonal);
    }

    private static FogState BuildFog(Rgb colour, double near, double far, double farVisibility, double worldDiagonal)
    {
        double begin = System.Math.Max(1.0, near);
        double end = System.Math.Max(System.Math.Max(begin + 1.0, far), worldDiagonal * 1.4);

        // Retail fog carries a far-plane visibility (0 opaque .. 1 clear); most
        // planets stay partly clear at the far plane, so drive Godot's density
        // from (1 - far visibility) and keep it a tint rather than a flat wall.
        double density = System.Math.Clamp(((1.0 - farVisibility) * 0.5) + 0.04, 0.04, 0.5);
        return new FogState(true, colour, begin, end, density, FogCurve);
    }
}
