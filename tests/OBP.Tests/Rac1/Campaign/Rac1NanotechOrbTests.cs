using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1NanotechOrbTests
{
    private static Rac1ParticleSession Session(uint seed = 3)
    {
        var textures = new[] { 1, 2, 3 }.ToDictionary(id => id, id => new RuntimeTexture("part", id, 1, 1, [255, 255, 255, 255]));
        var assets = new Rac1EffectAssets(new Dictionary<int, int[]>
        {
            [Rac1NanotechOrbAssets.ParticleTextureTable] = [1, 2],
            [Rac1NanotechOrbAssets.GlintTextureTable] = [3],
        }, textures, new Dictionary<int, RuntimeTexture>());
        return new(assets, Rac1PickupRandom.FromSeed(seed));
    }

    [Fact]
    public void CubicPassesThroughItsInnerPointsLikeTheNativeSpline()
    {
        Assert.Equal(5f, Rac1NanotechCluster.Cubic(1, 5, 9, 20, 0), 5);
        Assert.Equal(9f, Rac1NanotechCluster.Cubic(1, 5, 9, 20, 1), 5);
        Assert.Equal(7f, Rac1NanotechCluster.Cubic(5, 5, 9, 9, .5f), 5); // symmetric tangents: straight midpoint
    }

    [Fact]
    public void SharedOrbitKeepsEightDirectionsAtTheOrbRadiusWhileSpinning()
    {
        var spin = new Rac1NanotechOrbSpin();
        Assert.False(spin.Ready);
        spin.Initialize(.25f, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var before = spin.Directions.ToArray();
        for (int tick = 0; tick < 90; tick++) spin.Step(0.0698132f, .25f);
        Assert.All(spin.Directions, d => Assert.Equal(.25f, d.Length(), 4));
        Assert.NotEqual(before, spin.Directions);
        Assert.Equal(8, spin.Directions.Select(d => MathF.Round(d.X, 3) + "," + MathF.Round(d.Y, 3) + "," + MathF.Round(d.Z, 3)).Distinct().Count());
    }

    [Fact]
    public void SparklesFadeLinearlyAndOrbSpritesNeverFade()
    {
        var session = Session();
        var sparkle = session.Sparkle(Vector3.Zero, 40, 2, 10000)!;
        var orb = session.Sparkle(Vector3.Zero, 0, 1, 60000)!;
        Assert.Equal(0x7fu, sparkle.Color >> 24);
        Assert.Equal(10000 / Rac1ParticleSession.NativeSizeUnits, sparkle.Size, 5);
        for (int tick = 0; tick < 100; tick++) session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.False(session.IsLive(sparkle));
        Assert.True(session.IsLive(orb));
        Assert.Equal(0x7fu, orb.Color >> 24);
        // 127 / life per frame (truncated), freed once alpha reaches zero: 5 frames -> 25 per frame -> gone on the 6th tick.
        var fast = session.Sparkle(Vector3.Zero, 5, 0, 10000)!;
        for (int tick = 0; tick < 6; tick++) session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.False(session.IsLive(fast));
    }

    [Fact]
    public void PersistentOrbSpritesAreActuallyDrawnByTheSharedSpriteCapture()
    {
        // Regression: orbs carry no native life counter, and the capture skips particles with no life left.
        var session = Session();
        var orb = session.Sparkle(new(1, 2, 3), 0, 1, 60000)!;
        var textures = new[] { 1, 2, 3 }.ToDictionary(id => id, id => new RuntimeTexture("part", id, 1, 1, [255, 255, 255, 255]));
        var bank = new OBP.Core.ObpSourceRef(OBP.Core.ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, "LEVEL0");
        for (int tick = 0; tick < 30; tick++) session.Step(Vector3.Zero, enforceLevelBounds: false);
        var sprite = Assert.Single(Rac1ParticlePresentation.Capture(session, bank, textures).Sprites);
        Assert.Equal(60000 / Rac1ParticleSession.NativeSizeUnits, sprite.Size, 4);
        Assert.Equal(2, int.Parse(sprite.Texture.OriginalId)); // table 62 entry 1 = the orb texture
        Assert.True(session.IsLive(orb));
    }

    [Fact]
    public void HeroGlowsAndGlintsStayRelativeToRatchetAndExpire()
    {
        var session = Session();
        session.Hero = new(10, 0, 0);
        var glow = session.HeroGlow(new(10, 0, 1), 0x40ffffff, 7, false, 2, 4)!;
        var glint = session.Glint(new(10, 0, 1), new(0, 0, .05f), 0x50804040, 60, 3, .4f)!;
        Assert.Equal(4f, glow.Size, 4);
        session.Hero = new(20, 5, 0);
        session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.Equal(new Vector3(20, 5, 1), glow.Position);
        Assert.Equal(new Vector3(20, 5, 1.05f), glint.Position);
        session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.False(session.IsLive(glow));
        Assert.True(session.IsLive(glint));
        Assert.True(glint.Color >> 24 < 0x50);
    }

    private static (Rac1ExecutableOverlay Overlay, Rac1LevelCore.Core Core) Level(int id)
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == id);
        return (Rac1ExecutableOverlay.Read(disc, level), Rac1LevelCore.Open(disc, level));
    }

    [SkippableFact]
    public void EveryLevelCarriesTheAuthenticatedOrbTablesAtItsOwnAddresses()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        foreach (var level in Rac1DiscIndex.Read(disc).Levels)
        {
            var assets = Rac1NanotechOrbAssets.Decode(Rac1ExecutableOverlay.Read(disc, level), Rac1LevelCore.Open(disc, level));
            Assert.Equal(Rac1NanotechOrbAssets.ClusterPointCount, assets.ClusterPoints.Count);
            Assert.Equal(.25f, assets.K.OrbRadius);
            Assert.Equal(.06f, assets.K.BobAmplitude, 6);
            Assert.Equal(21, assets.K.GlowTexture);
            Assert.Equal(Rac1NanotechOrbAssets.RingQuadCount, assets.RingQuads.Count);
            Assert.True(assets.FxTextures.ContainsKey(21) && assets.FxTextures.ContainsKey(Rac1BeamSlot.EndpointTexture));
            Assert.Equal(2, assets.Particles.ParticleChoices[Rac1NanotechOrbAssets.ParticleTextureTable].Length);
            Assert.All(assets.Particles.ParticleChoices.Values.SelectMany(ids => ids), id => Assert.True(assets.Particles.PartTextures.ContainsKey(id)));
            Assert.All(assets.ClusterPoints, p => Assert.True(MathF.Abs(p.X) < 4 && MathF.Abs(p.Y) < 4 && MathF.Abs(p.Z) < 4));
        }
    }

    [SkippableFact]
    public void ClusterRidesThenLaunchesEightOrbsAndCreditsExactlyOneArrival()
    {
        var (overlay, core) = Level(0);
        var assets = Rac1NanotechOrbAssets.Decode(overlay, core);
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(9));
        var cluster = new Rac1NanotechCluster(new(100, 50, 3), assets, new(), particles, Rac1PickupRandom.FromSeed(11));
        var hero = new Vector3(105, 50, 3); var target = hero + new Vector3(0, 0, .7f);
        for (int tick = 0; tick < 30; tick++) cluster.Step(new(hero, target, Vector3.UnitZ, true));
        Assert.Equal(Rac1NanotechCluster.Phase.Riding, cluster.State);
        Assert.Equal(8, cluster.LiveOrbs);
        Assert.Contains(particles.Live, p => p.Type == Rac1ParticleSession.SparkleType && p.Stage == 1);
        Assert.InRange(MathF.Abs(cluster.Bob), 0, assets.K.BobAmplitude + 1e-5f);
        var centre = new Vector3(100, 50, 3.5f);
        Assert.All(cluster.OrbPositions, p => Assert.InRange(Vector3.Distance(p, centre), .18f, .32f));
        // Draw geometry: the 290-point mesh as three strips plus the 32-quad halo, all finite.
        var surfaces = cluster.Build(new(110, 55, 6));
        Assert.Equal(3 + Rac1NanotechOrbAssets.RingQuadCount, surfaces.Count);
        Assert.Equal(3, surfaces.Count(s => s.Texture == 21 && s.Blend == Rac1FxBlend.Alpha));
        Assert.Equal(32, surfaces.Count(s => s.Texture == Rac1BeamSlot.EndpointTexture && s.Blend == Rac1FxBlend.Additive));
        Assert.All(surfaces, s => Assert.All(s.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z))));

        cluster.CrateBroken();
        for (int tick = 0; tick < 10; tick++) cluster.Step(new(hero, target, Vector3.UnitZ, true));
        Assert.Equal(Rac1NanotechCluster.Phase.Resting, cluster.State);
        cluster.Launch(target, Vector3.UnitZ);
        Assert.Equal(Rac1NanotechCluster.Phase.Flying, cluster.State);
        int arrivals = 0, firstTick = -1;
        for (int tick = 1; tick <= 320 && cluster.State != Rac1NanotechCluster.Phase.Done; tick++)
        {
            var result = cluster.Step(new(hero, target, Vector3.UnitZ, true));
            particles.Step(Vector3.Zero, enforceLevelBounds: false);
            if (result.FirstArrival) { arrivals++; firstTick = tick; }
            if (tick < 20) Assert.Equal(8, cluster.LiveOrbs); // flight lives are 20..80 frames
        }
        Assert.Equal(1, arrivals);
        Assert.InRange(firstTick, 20, 80);
        Assert.True(cluster.Healed);
        Assert.Equal(Rac1NanotechCluster.Phase.Done, cluster.State);
        Assert.Equal(0, cluster.LiveOrbs);
        for (int tick = 0; tick < 120; tick++) particles.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.Equal(0, particles.Count); // nothing lingers after the cloud retires
    }

    [SkippableFact]
    public void DisposingAClusterReleasesItsOrbSprites()
    {
        var (overlay, core) = Level(0);
        var assets = Rac1NanotechOrbAssets.Decode(overlay, core);
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(2));
        var cluster = new Rac1NanotechCluster(Vector3.Zero, assets, new(), particles, Rac1PickupRandom.FromSeed(4));
        cluster.Step(new(Vector3.One * 50, Vector3.One * 50, Vector3.UnitZ, true));
        Assert.Equal(8, cluster.LiveOrbs);
        cluster.Dispose();
        Assert.Equal(0, cluster.LiveOrbs);
        Assert.DoesNotContain(particles.Live, p => p.Stage == 1);
    }
}
