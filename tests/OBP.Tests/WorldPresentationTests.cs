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
        Assert.Equal(new Rgb(0.1, 0.2, 0.3),
            WorldPresentation.Resolve(Env(background: (0.1, 0.2, 0.3), fog: (0.9, 0.9, 0.9)), UnitBounds).Background);

        Assert.Equal(new Rgb(0.4, 0.5, 0.6),
            WorldPresentation.Resolve(Env(fog: (0.4, 0.5, 0.6)), UnitBounds).Background);

        Assert.Equal(WorldPresentation.DefaultBackground,
            WorldPresentation.Resolve(Env(), UnitBounds).Background);

        Assert.Equal(WorldPresentation.DefaultBackground,
            WorldPresentation.Resolve(null, UnitBounds).Background);
    }

    [Fact]
    public void Ambient_LiftsTowardWhite_AndDefaultsToWhite()
    {
        Assert.Equal(new Rgb(0.55, 0.55, 0.55),
            WorldPresentation.Resolve(Env(ambient: (0, 0, 0)), UnitBounds).Ambient);

        Assert.Equal(Rgb.White, WorldPresentation.Resolve(Env(ambient: (1, 1, 1)), UnitBounds).Ambient);

        Assert.Equal(Rgb.White, WorldPresentation.Resolve(Env(), UnitBounds).Ambient);
        Assert.Equal(1.0, WorldPresentation.Resolve(Env(), UnitBounds).AmbientEnergy);
    }

    [Fact]
    public void ToneMap_IsAgx_WithNeutralExposure_WhenNoAmbient()
    {
        var tm = WorldPresentation.Resolve(Env(), UnitBounds).ToneMap;
        Assert.Equal(ToneMapMode.Agx, tm.Mode);
        Assert.Equal(1.0, tm.Exposure);
    }

    [Fact]
    public void ToneMap_Exposure_OpensDarkPlanetsAndPullsBackBrightOnes()
    {
        double dark = WorldPresentation.ResolveToneMap(Env(ambient: (0.11, 0.18, 0.20))).Exposure;
        double bright = WorldPresentation.ResolveToneMap(Env(ambient: (0.7, 0.7, 0.7))).Exposure;

        Assert.True(dark > 1.0, $"dark ambient should raise exposure, got {dark}");
        Assert.True(bright < 1.0, $"bright ambient should lower exposure, got {bright}");
        Assert.InRange(dark, 0.9, 1.15);
        Assert.InRange(bright, 0.9, 1.15);
    }

    [Fact]
    public void Grade_IsAGentleFixedLift()
    {
        var grade = WorldPresentation.Resolve(Env(ambient: (0.2, 0.2, 0.2)), UnitBounds).Grade;
        Assert.False(grade.IsNeutral);
        Assert.Equal(1.0, grade.Brightness);
        Assert.InRange(grade.Contrast, 1.0, 1.15);
        Assert.InRange(grade.Saturation, 1.0, 1.15);
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
    public void Fog_EndPlane_StretchedPastTheWorldDiagonal()
    {
        // near/far are tiny but the world is large: the end plane must reach past
        // 1.4x the diagonal so distant scenery still reads.
        var big = new ObpBounds(new Vec3(0, 0, 0), new Vec3(1000, 0, 0));
        var fog = WorldPresentation.ResolveFog(Env(fog: (0.5, 0.5, 0.5), fogNear: 5, fogFar: 20), big);

        Assert.True(fog.Enabled);
        Assert.Equal(5.0, fog.Begin);
        Assert.Equal(1000.0 * 1.4, fog.End, precision: 6);
        Assert.Equal(WorldPresentation.FogCurve, fog.Curve);
    }

    [Theory]
    [InlineData(255f, 0.04)]      // fully visible at the far plane -> minimum density
    [InlineData(0f, 0.5)]         // opaque at the far plane -> clamped maximum
    [InlineData(128f, 0.289020)]  // (1 - 128/255) * 0.5 + 0.04
    public void Fog_Density_DrivenByFarVisibility_AndClamped(float farIntensity, double expected)
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
        Assert.Equal(System.Math.Max(250.0, UnitBounds.Diagonal * 1.4), fog.End, precision: 6);
        Assert.InRange(fog.Density, 0.04, 0.5);
    }

    [Fact]
    public void FogFromResolved_Disabled_WhenRegionHasNoFog()
    {
        var noFog = new EnvResolved(Rgb.White, false, new Vec3(0, -1, 0), Rgb.White, false, default, 0, 0, 1);
        Assert.Equal(FogState.Disabled, WorldPresentation.FogFromResolved(noFog, 100));
    }
}
