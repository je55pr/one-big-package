using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Pure presentation maths: the load-time <see cref="PresentationState"/> and the
/// per-frame <see cref="FogState"/> resolution that used to be duplicated inside
/// <c>OBP.Godot.RuntimeWorldScene</c> and the Godot host.
/// </summary>
public class WorldPresentationTests
{
    private static readonly ObpBounds UnitBounds = new(new Vec3(0, 0, 0), new Vec3(100, 100, 100));

    private static RuntimeEnvironment Env(
        (double, double, double)? background = null,
        (double, double, double)? fog = null,
        float fogNear = 0,
        float fogFar = 0,
        float fogFarIntensity = 255,
        (double, double, double)? ambient = null) =>
        new(
            DeathHeight: 0,
            IsSphericalWorld: false,
            BackgroundColour: background,
            FogColour: fog,
            FogNearDistance: fogNear,
            FogFarDistance: fogFar,
            FogNearIntensity: 255,
            FogFarIntensity: fogFarIntensity,
            AmbientColour: ambient);

    [Fact]
    public void Background_PrefersExplicitColour_ThenFog_ThenDefault()
    {
        var explicitBackground = WorldPresentation.Resolve(
            Env(background: (0.1, 0.2, 0.3), fog: (0.9, 0.9, 0.9)), UnitBounds);
        Assert.Equal(new Rgb(0.1, 0.2, 0.3), explicitBackground.Background);
        Assert.Equal(RuntimeAtmosphereSource.NativeLevelSettings, explicitBackground.BackgroundSource);

        var fogBackground = WorldPresentation.Resolve(Env(fog: (0.4, 0.5, 0.6)), UnitBounds);
        Assert.Equal(new Rgb(0.4, 0.5, 0.6), fogBackground.Background);
        Assert.Equal(RuntimeAtmosphereSource.PresentationFallback, fogBackground.BackgroundSource);

        var fallback = WorldPresentation.Resolve(Env(), UnitBounds);
        Assert.Equal(WorldPresentation.DefaultBackground, fallback.Background);
        Assert.Equal(RuntimeAtmosphereSource.PresentationFallback, fallback.BackgroundSource);

        Assert.Equal(WorldPresentation.DefaultBackground, WorldPresentation.Resolve(null, UnitBounds).Background);
    }

    [Fact]
    public void Ambient_PreservesRecoveredValue_AndLabelsWhiteFallback()
    {
        var native = Env(ambient: (0.11, 0.18, 0.20)) with
        {
            AmbientSource = RuntimeAtmosphereSource.NativeEnvironmentSample,
        };
        var nativeState = WorldPresentation.Resolve(native, UnitBounds);
        Assert.Equal(new Rgb(0.11, 0.18, 0.20), nativeState.Ambient);
        Assert.Equal(RuntimeAtmosphereSource.NativeEnvironmentSample, nativeState.AmbientSource);

        var fallback = WorldPresentation.Resolve(Env(), UnitBounds);
        Assert.Equal(Rgb.White, fallback.Ambient);
        Assert.Equal(RuntimeAtmosphereSource.PresentationFallback, fallback.AmbientSource);
        Assert.Equal(1.0, fallback.AmbientEnergy);
    }

    [Fact]
    public void Atmosphere_DoesNotInventToneMapExposureOrGrade()
    {
        var state = WorldPresentation.Resolve(Env(ambient: (0.11, 0.18, 0.20)), UnitBounds);

        Assert.Equal(ToneMap.Neutral, state.ToneMap);
        Assert.Equal(ColourGrade.Neutral, state.Grade);
        Assert.Equal(ToneMap.Neutral, WorldPresentation.ResolveToneMap(Env(ambient: (0.7, 0.7, 0.7))));
        Assert.Equal(ColourGrade.Neutral, WorldPresentation.ResolveGrade(Env()));
    }

    [Theory]
    [InlineData(null, 10f, 100f)]   // no fog colour
    [InlineData("grey", 100f, 50f)] // far <= near
    [InlineData("grey", 10f, 0f)]   // far <= 0
    public void Fog_Disabled_WhenLevelDefinesNone(string? colour, float near, float far)
    {
        var env = Env(fog: colour is null ? null : (0.5, 0.5, 0.5), fogNear: near, fogFar: far);
        Assert.Equal(FogState.Disabled, WorldPresentation.ResolveFog(env, UnitBounds));
    }

    [Fact]
    public void Fog_PreservesRecoveredNearAndFarPlanes()
    {
        var big = new ObpBounds(new Vec3(0, 0, 0), new Vec3(1000, 0, 0));
        var environment = Env(fog: (0.5, 0.5, 0.5), fogNear: 5, fogFar: 20) with
        {
            FogSource = RuntimeAtmosphereSource.NativeEnvironmentSample,
        };
        var state = WorldPresentation.Resolve(environment, big);
        var fog = state.Fog;

        Assert.True(fog.Enabled);
        Assert.Equal(5.0, fog.Begin);
        Assert.Equal(20.0, fog.End, precision: 6);
        Assert.Equal(1.0, fog.Curve);
        Assert.Equal(RuntimeAtmosphereSource.NativeEnvironmentSample, state.FogSource);
    }

    [Theory]
    [InlineData(255f, 0.0)]
    [InlineData(0f, 1.0)]
    [InlineData(128f, 0.498039)]
    public void Fog_Density_UsesRecoveredFarVisibility(float farIntensity, double expected)
    {
        var fog = WorldPresentation.ResolveFog(
            Env(fog: (0.5, 0.5, 0.5), fogNear: 5, fogFar: 400, fogFarIntensity: farIntensity), UnitBounds);
        Assert.Equal(expected, fog.Density, precision: 6);
    }

    [Fact]
    public void FogFromResolved_MatchesResolveFog_Structurally()
    {
        var resolved = new EnvResolved(
            Ambient: Rgb.White,
            HasHeroLight: false,
            HeroTravel: new Vec3(0, -1, 0),
            HeroColour: Rgb.White,
            HasFog: true,
            FogColour: new Rgb(0.6, 0.65, 0.47),
            FogNear: 50,
            FogFar: 250,
            FogFarVisibility: 0.5);

        var fog = WorldPresentation.FogFromResolved(resolved, UnitBounds.Diagonal);
        Assert.True(fog.Enabled);
        Assert.Equal(new Rgb(0.6, 0.65, 0.47), fog.Colour);
        Assert.Equal(50.0, fog.Begin);
        Assert.Equal(250.0, fog.End, precision: 6);
        Assert.Equal(0.5, fog.Density, precision: 6);
    }

    [Fact]
    public void FogFromResolved_Disabled_WhenRegionHasNoFog()
    {
        var noFog = new EnvResolved(Rgb.White, false, new Vec3(0, -1, 0), Rgb.White, false, default, 0, 0, 1);
        Assert.Equal(FogState.Disabled, WorldPresentation.FogFromResolved(noFog, 100));
    }
}
