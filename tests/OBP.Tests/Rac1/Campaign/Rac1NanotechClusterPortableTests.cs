using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>Cluster lifecycle against synthetic constants and geometry, so it runs without the retail disc.</summary>
public sealed class Rac1NanotechClusterPortableTests
{
    private static Rac1NanotechOrbAssets Assets()
    {
        var k = new Rac1NanotechOrbAssets.Constants(
            SpinStep: 0.0698132f, OrbFrameStep: 4, SparkleSize: 10000, OrbSize: 60000, SparkleColor: 0x7f7f4040, BobStep: 0.0349066f,
            BobAmplitude: .06f, OrbRadius: .25f, TrailFrames: 90, BreathAmplitude: .15f, GlintSpeedLow: 1, GlintSpeedHigh: 2, GlintLife: 60,
            GlintColorA: 0x50804040, GlintColorB: 0x407f7f7f, GlintSizeA: .4f, GlintSizeB: .2f, TrailColorA: 0x407f2020, TrailColorB: 0x107f2020,
            TrailFrameSteps: [-2, -4.25f, 1.25f, 3.5f], TrailSizes: [4, 5, 3, 6], TrailHeights: [-.25f, -.1f, .15f, .3f], TargetOffset: .75f,
            CurveSide: 5, FlightLifeLow: 20, FlightLifeHigh: 80, FlightTimer: 300, GlowTexture: 21, RingScale: .3f, RingColorLow: 0x00802020,
            RingOuterScale: 1.125f);
        var textures = new[] { 1, 2, 3, 4 }.ToDictionary(id => id, id => new RuntimeTexture("part", id, 1, 1, [255, 255, 255, 255]));
        var particles = new Rac1EffectAssets(new Dictionary<int, int[]>
        {
            [Rac1NanotechOrbAssets.ParticleTextureTable] = [1, 2],
            [Rac1NanotechOrbAssets.GlintTextureTable] = [3],
            [Rac1ParticleSession.SparkTextureTable] = [4],
        }, textures, new Dictionary<int, RuntimeTexture>());
        return new(k, new Vector3[Rac1NanotechOrbAssets.ClusterPointCount], new Vector2[Rac1NanotechOrbAssets.ClusterPointCount],
            new uint[Rac1NanotechOrbAssets.ClusterPointCount], new Vector3[Rac1NanotechOrbAssets.RingVertexCount],
            Enumerable.Range(0, Rac1NanotechOrbAssets.RingQuadCount).Select(_ => new short[] { 0, 1, 2, 3 }).ToArray(),
            [.06981317f, .03490658f, .1047198f, .05235988f, .01745329f, .08726646f, .04363323f, .07853982f],
            new Dictionary<int, RuntimeTexture>(), particles);
    }

    private static readonly Vector3 Hero = new(5, 0, 0), Target = new(5, 0, .7f);

    [Fact]
    public void CloudLaunchedWithRoomForItsSpritesCreditsExactlyOneArrivalAndRetires()
    {
        var assets = Assets();
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(3));
        var cluster = new Rac1NanotechCluster(Vector3.Zero, assets, new(), particles, Rac1PickupRandom.FromSeed(7));
        cluster.CrateBroken();
        cluster.Launch(Target, Vector3.UnitZ);
        int arrivals = 0, first = -1;
        for (int tick = 1; tick <= 400 && cluster.State != Rac1NanotechCluster.Phase.Done; tick++)
        {
            var result = cluster.Step(new(Hero, Target, Vector3.UnitZ, true));
            particles.Step(Vector3.Zero, enforceLevelBounds: false);
            if (result.FirstArrival) { arrivals++; first = tick; }
        }
        Assert.Equal(1, arrivals);
        Assert.InRange(first, 20, 80);
        Assert.True(cluster.Healed);
        Assert.Equal(Rac1NanotechCluster.Phase.Done, cluster.State);
        Assert.Equal(0, cluster.ActiveOrbs);
        for (int tick = 0; tick < 200; tick++) particles.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.Equal(0, particles.Count);
    }

    [Fact]
    public void FullParticlePoolStillDeliversTheHealthCreditBecauseFlightDoesNotNeedASprite()
    {
        var assets = Assets();
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(3), 16);
        while (particles.Spark(Vector3.Zero, Vector3.Zero, 1000, 10000) is not null) { } // every slot taken by long-lived sparks
        var cluster = new Rac1NanotechCluster(Vector3.Zero, assets, new(), particles, Rac1PickupRandom.FromSeed(7));
        cluster.CrateBroken();
        cluster.Launch(Target, Vector3.UnitZ);
        Assert.Equal(0, cluster.LiveOrbs);   // no free slot for a single orb sprite ...
        Assert.Equal(8, cluster.ActiveOrbs); // ... yet all eight orbs are flying
        int arrivals = 0;
        for (int tick = 1; tick <= 400 && cluster.State != Rac1NanotechCluster.Phase.Done; tick++)
            if (cluster.Step(new(Hero, Target, Vector3.UnitZ, true)).FirstArrival) arrivals++;
        Assert.Equal(1, arrivals);
        Assert.True(cluster.Healed);
    }

    [Fact]
    public void ClusterRestingInvisiblyKeepsItsExistingOrbSpritesWithTheCluster()
    {
        var assets = Assets();
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(4));
        var cluster = new Rac1NanotechCluster(new(1, 2, 3), assets, new(), particles, Rac1PickupRandom.FromSeed(5));
        for (int tick = 0; tick < 5; tick++) cluster.Step(new(Hero, Target, Vector3.UnitZ, true));
        Assert.Equal(8, cluster.LiveOrbs);
        cluster.Follow(new(11, 12, 13)); // the crate moved while the cluster is out of view
        cluster.Step(new(Hero, Target, Vector3.UnitZ, false));
        var centre = new Vector3(11, 12, 13.5f);
        Assert.All(particles.Live.Where(p => p.Stage == 1), p => Assert.InRange(Vector3.Distance(p.Position, centre), .15f, .35f));
        int before = particles.Count;
        cluster.Step(new(Hero, Target, Vector3.UnitZ, false));
        Assert.Equal(before, particles.Count); // hidden clusters spawn no new sparkles
    }

    [Fact]
    public void ClusterLaunchedBeforeItsFirstTickStillSpreadsItsOrbsAlongTheOrbit()
    {
        var assets = Assets();
        var particles = new Rac1ParticleSession(assets.Particles, Rac1PickupRandom.FromSeed(5));
        var cluster = new Rac1NanotechCluster(new(10, 10, 3), assets, new(), particles, Rac1PickupRandom.FromSeed(6));
        cluster.CrateBroken();
        cluster.Launch(new(15, 10, 3.7f), Vector3.UnitZ); // no Step first
        var centre = new Vector3(10, 10, 3.5f);
        Assert.All(cluster.OrbPositions, p => Assert.InRange(Vector3.Distance(p, centre), .24f, .26f));
        Assert.Equal(8, cluster.OrbPositions.Select(p => (MathF.Round(p.X, 2), MathF.Round(p.Y, 2), MathF.Round(p.Z, 2))).Distinct().Count());
    }

    [Fact]
    public void ForkedPresentationStreamsAreDeterministicAndConsumeExactlyTwoDraws()
    {
        var a = Rac1PickupRandom.FromSeed(5); var b = Rac1PickupRandom.FromSeed(5);
        var fork = a.Fork();
        b.Below(32768); b.Below(32768);
        Assert.Equal(b.Below(1000), a.Below(1000)); // the parent advanced by two draws only
        var again = Rac1PickupRandom.FromSeed(5).Fork();
        Assert.Equal(fork.Below(1000), again.Below(1000));
    }

    [SkippableFact]
    public void NarrowedSecondSocketLookupNeverRejectsAnyClassOfAnyRetailLevel()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        int withSecond = 0;
        foreach (var level in Rac1DiscIndex.Read(disc).Levels)
        {
            var core = Rac1LevelCore.Open(disc, level);
            foreach (var cls in Rac1StaticClasses.Read(core).Mobies.Values)
            {
                var asset = core.Assets.AsMemory(cls.AssetOffset, cls.AssetLength);
                try { Rac1MobyAttachment.ReadJoint(asset.Span, 0); }
                catch (InvalidDataException) { continue; } // never baked: no semantic socket 0 either
                if (!Rac1MobyAttachment.HasJoint(asset.Span, 1)) continue;
                Rac1MobyAttachment.ReadJoint(asset.Span, 1); // a declared second socket must be well formed
                withSecond++;
            }
        }
        Assert.True(withSecond > 0);
    }

    [Fact]
    public void BlockSearchFindsTheLastAlignedOffsetAndIgnoresUnalignedNearMisses()
    {
        var block = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var data = new byte[64];
        block.CopyTo(data, 56); // last aligned slot
        block.CopyTo(data, 2);  // unaligned near-miss
        var payload = new byte[16 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0), 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), 0x100000);
        data.CopyTo(payload, 16);
        Assert.Equal([0x100038], Rac1ExecutableOverlay.Parse(payload).FindBlock(block));

        // A block that occurs only at an unaligned offset is never reported.
        var unaligned = new byte[64];
        var rare = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        rare.CopyTo(unaligned, 2);
        unaligned.CopyTo(payload, 16);
        Assert.Empty(Rac1ExecutableOverlay.Parse(payload).FindBlock(rare));
    }
}
