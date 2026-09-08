using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// Pure per-region lighting/fog resolution promoted out of the Godot host
/// (<c>game/scripts/EnvProbe.cs</c>). Nearest env sample, fog fallback and
/// env-transition doorway blend. See <c>research/GC_LIGHTING.md</c>.
/// </summary>
public class EnvResolverTests
{
    private static RuntimeEnvSample Sample(
        (double, double, double) pos, int heroIdx, (double, double, double) heroCol, RuntimeFog? fog = null) =>
        new(pos, heroIdx, heroCol, fog);

    private static RuntimeFog Fog((double, double, double) colour, double near = 50, double far = 250, double farInt = 128) =>
        new(colour, near, far, 255, farInt);

    private static RuntimeDirLight Dir((double, double, double) colour, (double, double, double) travel) =>
        new(colour, travel, (0, 0, 0), (0, 0, 0));

    private static RuntimeLighting Lighting(
        IReadOnlyList<RuntimeDirLight>? dirs = null,
        IReadOnlyList<RuntimeEnvSample>? samples = null,
        IReadOnlyList<RuntimeEnvTransition>? transitions = null) =>
        new(dirs ?? [], samples ?? [], transitions ?? []);

    [Fact]
    public void NearestEnvSample_SetsAmbientAndHeroIndex()
    {
        var lighting = Lighting(
            dirs: [Dir((1, 0, 0), (0, -1, 0)), Dir((0, 1, 0), (0, -1, 0))],
            samples:
            [
                Sample((-100, 0, 0), heroIdx: 0, heroCol: (0.1, 0.1, 0.1)),
                Sample((100, 0, 0), heroIdx: 1, heroCol: (0.7, 0.6, 0.5)),
            ]);

        var r = EnvResolver.Evaluate(lighting, new Vec3(90, 0, 0));

        Assert.Equal(new Rgb(0.7, 0.6, 0.5), r.Ambient);
        Assert.True(r.HasHeroLight);
        Assert.Equal(new Rgb(0, 1, 0), r.HeroColour); // DirLights[1].ColourA
    }

    [Fact]
    public void Fog_FallsBackToNearestSampleThatDefinesIt()
    {
        var lighting = Lighting(samples:
        [
            Sample((0, 0, 0), heroIdx: -1, heroCol: (0.2, 0.2, 0.2), fog: null),
            Sample((40, 0, 0), heroIdx: -1, heroCol: (0.2, 0.2, 0.2), fog: Fog((0.6, 0.65, 0.47))),
            Sample((500, 0, 0), heroIdx: -1, heroCol: (0.2, 0.2, 0.2), fog: Fog((0.1, 0.1, 0.1))),
        ]);

        var r = EnvResolver.Evaluate(lighting, new Vec3(0, 0, 0));

        Assert.True(r.HasFog);
        Assert.Equal(new Rgb(0.6, 0.65, 0.47), r.FogColour);
    }

    [Fact]
    public void NoFogAnywhere_LeavesFarVisibilityClear()
    {
        var r = EnvResolver.Evaluate(
            Lighting(samples: [Sample((0, 0, 0), -1, (0.3, 0.3, 0.3))]), new Vec3(0, 0, 0));

        Assert.False(r.HasFog);
        Assert.Equal(1.0, r.FogFarVisibility);
    }

    [Fact]
    public void OutOfRangeHeroIndex_MeansAmbientOnly_WithDefaultTravel()
    {
        // 3855 is the retail out-of-range sentinel (see GC_LIGHTING.md).
        var lighting = Lighting(
            dirs: [Dir((1, 1, 1), (0, -1, 0))],
            samples: [Sample((0, 0, 0), heroIdx: 3855, heroCol: (0.25, 0.25, 0.25))]);

        var r = EnvResolver.Evaluate(lighting, new Vec3(0, 0, 0));

        Assert.False(r.HasHeroLight);
        Assert.Equal(new Rgb(0.25, 0.25, 0.25), r.Ambient);
        Assert.Equal(new Rgb(0.6, 0.6, 0.6), r.HeroColour);
        Assert.Equal(1.0, r.HeroTravel.Y * r.HeroTravel.Y + (r.HeroTravel.X * r.HeroTravel.X) + (r.HeroTravel.Z * r.HeroTravel.Z), precision: 6);
    }

    [Fact]
    public void HeroTravel_IsTheNormalisedDirLightDirection()
    {
        var lighting = Lighting(
            dirs: [Dir((0.9, 0.9, 0.7), (0, 4, 0))], // non-unit on purpose
            samples: [Sample((0, 0, 0), heroIdx: 0, heroCol: (0.3, 0.3, 0.3))]);

        var r = EnvResolver.Evaluate(lighting, new Vec3(0, 0, 0));

        Assert.True(r.HasHeroLight);
        Assert.Equal(0.0, r.HeroTravel.X, precision: 6);
        Assert.Equal(1.0, r.HeroTravel.Y, precision: 6);
        Assert.Equal(0.0, r.HeroTravel.Z, precision: 6);
    }

    [Fact]
    public void EnvTransition_BlendsHeroColourAlongLocalX()
    {
        // Inverse matrix maps local X = world X (column-major identity first row).
        double[] inv = new double[16];
        inv[0] = 1; // local x = p.X
        var transition = new RuntimeEnvTransition(
            InverseMatrixZUp: inv,
            BoundingSphere: (0, 0, 0, 1000),
            BlendHero: true,
            BlendFog: false,
            A: new RuntimeEnvState((0, 0, 0), 0, Fog((0, 0, 0))),
            B: new RuntimeEnvState((1, 1, 1), 1, Fog((0, 0, 0))));

        var lighting = Lighting(
            dirs: [Dir((0.1, 0, 0), (0, -1, 0)), Dir((0, 0, 0.1), (0, -1, 0))],
            samples: [Sample((0, 0, 0), heroIdx: 0, heroCol: (0.5, 0.5, 0.5))],
            transitions: [transition]);

        // p.X = 0 -> blend 0.5 -> ambient = lerp(black, white, 0.5)
        var mid = EnvResolver.Evaluate(lighting, new Vec3(0, 0, 0));
        Assert.Equal(new Rgb(0.5, 0.5, 0.5), mid.Ambient);

        // p.X = +5 -> lx 5 -> blend clamps to 1 -> ambient = white, hero index = B's (1)
        var far = EnvResolver.Evaluate(lighting, new Vec3(5, 0, 0));
        Assert.Equal(new Rgb(1, 1, 1), far.Ambient);
        Assert.Equal(new Rgb(0, 0, 0.1), far.HeroColour); // DirLights[1].ColourA
    }

    [Fact]
    public void EnvTransition_FogFlag_GatesFogBlendIndependentlyOfHero()
    {
        double[] inv = new double[16];
        inv[0] = 1;
        var transition = new RuntimeEnvTransition(
            InverseMatrixZUp: inv,
            BoundingSphere: (0, 0, 0, 1000),
            BlendHero: false,
            BlendFog: true,
            A: new RuntimeEnvState((0, 0, 0), -1, Fog((0.2, 0.2, 0.2), near: 10, far: 100)),
            B: new RuntimeEnvState((0, 0, 0), -1, Fog((0.8, 0.8, 0.8), near: 30, far: 300)));

        var lighting = Lighting(
            samples: [Sample((0, 0, 0), heroIdx: -1, heroCol: (0.4, 0.4, 0.4), fog: Fog((0.1, 0.1, 0.1)))],
            transitions: [transition]);

        var r = EnvResolver.Evaluate(lighting, new Vec3(0, 0, 0)); // blend 0.5

        Assert.Equal(new Rgb(0.4, 0.4, 0.4), r.Ambient);   // hero untouched
        Assert.Equal(new Rgb(0.5, 0.5, 0.5), r.FogColour); // fog blended A<->B
        Assert.Equal(20.0, r.FogNear, precision: 6);
        Assert.Equal(200.0, r.FogFar, precision: 6);
    }
}
