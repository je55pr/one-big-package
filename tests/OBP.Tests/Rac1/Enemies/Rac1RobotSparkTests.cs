using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1RobotSparkTests
{
    private static Rac1ParticleSession Session(uint seed = 5)
    {
        var textures = new Dictionary<int, RuntimeTexture> { [0] = new("part", 0, 1, 1, [255, 255, 255, 255]) };
        var assets = new Rac1EffectAssets(new Dictionary<int, int[]> { [Rac1ParticleSession.SparkTextureTable] = [0] }, textures,
            new Dictionary<int, RuntimeTexture>());
        return new(assets, Rac1PickupRandom.FromSeed(seed));
    }

    [Fact]
    public void SparkDriftsAndFadesLinearlyThenExpires()
    {
        var session = Session();
        var spark = session.Spark(Vector3.Zero, new(.01f, 0, 0), 20000, 10)!;
        Assert.Equal(0x7fu, spark.Color >> 24);
        Assert.Equal(20000 / Rac1ParticleSession.NativeSizeUnits, spark.Size, 5);
        for (int tick = 1; tick < 10; tick++)
        {
            session.Step(Vector3.Zero, enforceLevelBounds: false);
            Assert.True(session.IsLive(spark));
            Assert.Equal((uint)(int)((10 - tick) / 10f * 0x7f), spark.Color >> 24);
            Assert.Equal(0x7f7f7fu, spark.Color & 0xffffff);
            Assert.Equal(.01f * tick, spark.Position.X, 5);
        }
        session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.False(session.IsLive(spark));
    }

    [Fact]
    public void DefaultSparksDrawTheNativeSizeAndLifeRanges()
    {
        var session = Session(9);
        var all = Enumerable.Range(0, 200).Select(_ => session.Spark(Vector3.Zero, Vector3.Zero)!).ToArray();
        Assert.All(all, p => { Assert.InRange(p.Size * Rac1ParticleSession.NativeSizeUnits, 8000, 50000); Assert.InRange(p.Total, 10, 29); });
        Assert.True(all.Select(p => p.Total).Distinct().Count() > 5);
    }

    [Fact]
    public void LampFlashIsOneDriftingSparkAndThreeTwoFrameFlaresAtTheLoweredSocket()
    {
        var session = Session(3);
        var lamp = new Vector3(4, 5, 6);
        Rac1RobotLampFlash.Emit(session, Rac1PickupRandom.FromSeed(7), lamp);
        var live = session.Live.ToArray();
        Assert.Equal(4, live.Length);
        Assert.All(live, p => Assert.Equal(lamp - Vector3.UnitZ * .333f, p.Position));
        var drift = Assert.Single(live, p => p.Velocity != Vector3.Zero);
        Assert.InRange(drift.Size * Rac1ParticleSession.NativeSizeUnits, 6000, 32000);
        Assert.InRange(drift.Velocity.Length(), .005f, .03f + 1e-5f);
        var flares = live.Where(p => p.Velocity == Vector3.Zero).ToArray();
        Assert.Equal(3, flares.Length);
        Assert.All(flares, p => { Assert.Equal(2, p.Total); Assert.InRange(p.Size * Rac1ParticleSession.NativeSizeUnits, 80000, 180000); });
        for (int tick = 0; tick < 2; tick++) session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.All(flares, p => Assert.False(session.IsLive(p)));
        // Every tick a fresh set is emitted, so the lamp always carries about four sparks.
        int bigThird = 0;
        for (int tick = 0; tick < 400; tick++)
        {
            var before = session.Live.ToHashSet();
            Rac1RobotLampFlash.Emit(session, Rac1PickupRandom.FromSeed((uint)tick), lamp);
            if (session.Live.Any(p => !before.Contains(p) && p.Size * Rac1ParticleSession.NativeSizeUnits > 150000)) bigThird++;
        }
        Assert.InRange(bigThird, 30, 70); // one tick in eight draws the 180000 flare
    }

    [Fact]
    public void BeamEndsEmitDriftingSparksEveryTickAndFlaresAboutOneTickInThree()
    {
        var slot = new Rac1BeamSlot();
        var origin = new Vector3(10, 20, 3); var target = new Vector3(15, 20, 3);
        var random = Rac1PickupRandom.FromSeed(2);
        var session = Session(4);
        int flareTicks = 0; var angles = new List<float>();
        for (int tick = 0; tick < 600; tick++)
        {
            slot.Step(origin, target, random);
            var before = session.Live.ToHashSet();
            slot.EmitSparks(session, random);
            var fresh = session.Live.Where(p => !before.Contains(p)).ToArray();
            Assert.Equal(2, fresh.Count(p => p.Velocity != Vector3.Zero)); // muzzle spark + tip spark
            flareTicks += fresh.Count(p => p.Velocity == Vector3.Zero && p.Total == 4);
            angles.Add(MathF.Acos(Vector3.Dot(Vector3.Normalize(fresh.First(p => p.Position == origin && p.Velocity != Vector3.Zero).Velocity), Vector3.UnitX)));
            session.Clear();
        }
        // Two independent one-in-three flares per tick.
        Assert.InRange(flareTicks / 600f, 2 / 3f * .75f, 2 / 3f * 1.3f);
        // Muzzle sparks leave 20..40 degrees off the beam axis.
        Assert.All(angles, a => Assert.InRange(a * 180 / MathF.PI, 19, 41));
    }

    [SkippableFact]
    public void EveryLevelResolvesTheSparkTextureThroughTheSharedPartLookup()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        foreach (var level in Rac1DiscIndex.Read(disc).Levels)
        {
            var assets = Rac1EffectAssets.DecodeSparks(Rac1LevelCore.Open(disc, level));
            int id = Assert.Single(assets.ParticleChoices[Rac1ParticleSession.SparkTextureTable]);
            Assert.True(assets.PartTextures.ContainsKey(id));
            Assert.Contains(assets.PartTextures[id].Rgba.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
        }
    }
}
