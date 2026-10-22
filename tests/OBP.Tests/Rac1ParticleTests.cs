using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1ParticleTests
{
    private static Rac1EffectAssets SyntheticAssets() => new(
        new Dictionary<int, int[]>
        {
            [4] = [4],
            [8] = Enumerable.Range(80, 10).ToArray(),
            [11] = [11],
            [12] = Enumerable.Range(12, 8).ToArray(),
            [15] = [15],
            [16] = [160, 161, 162],
            [24] = [24]
        },
        new Dictionary<int, RuntimeTexture>(), new Dictionary<int, RuntimeTexture>());

    [Fact]
    public void FlameMovesRisesGrowsAndFadesOnItsSourceClock()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        var p = session.Flame(new(100, 100, 100), new(.2f, 0, 0), 12, false)!;
        Assert.Equal(20, p.Remaining);
        Assert.Equal(Rac1ParticleSession.Blend.Alpha, p.Blending);
        Assert.Equal(.25f, p.Size);
        session.Step(Vector3.Zero);
        Assert.Equal(new Vector3(100.2f, 100, 100 + 1f / 60), p.Position);
        Assert.Equal(19, p.Remaining);
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.Equal(.75f, p.Size, 5);
        Assert.Equal(28u, p.Color >> 24);
        Assert.Equal(11, p.Angle);
        for (int i = 0; i < 30; i++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void FlameHasTwoDistinctLooksAndEightShortTextureChoices()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 7));
        var shortFlame = session.Flame(new(100), Vector3.UnitX / 60, 12, false)!;
        var longFlame = session.Flame(new(100), Vector3.UnitX / 60, 12, true)!;
        Assert.Equal(19, shortFlame.Texture);
        Assert.Equal(24, longFlame.Texture);
        Assert.Equal(Rac1ParticleSession.Blend.Additive, longFlame.Blending);
        Assert.Equal(1, longFlame.Size);
        Assert.InRange(shortFlame.Remaining, 20, 29);
        Assert.InRange(longFlame.Remaining, 25, 34);
    }

    [Fact]
    public void PyrocitorReducesItsLongFlameEmissionNearAnObstruction()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), Rac1PickupRandom.FromSeed(121));
        var full = session.Pyrocitor(new(100), Vector3.UnitX, 12);
        Assert.Equal(4, full.Count);
        Assert.Equal(2, full.Count(p => p.Blending == Rac1ParticleSession.Blend.Additive));
        Assert.All(full, p => Assert.InRange(p.Velocity.Length(), .24f, .29f));
        session.Clear();
        var blocked = session.Pyrocitor(new(100), Vector3.UnitX, 3);
        Assert.Equal(3, blocked.Count);
        Assert.Single(blocked, p => p.Blending == Rac1ParticleSession.Blend.Additive);
    }

    [Fact]
    public void ExplosionSplitsAtTheSourceAgeIntoTwoGenerationsThenBecomesSmoke()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        var p = session.ExplosionSpark(new(100), Vector3.UnitZ / 30, 0x4f008fff, 0x2f005f7f, 15, 25, 0, 0, 400000, .1f)!;
        for (int i = 0; i < 3; i++) session.Step(Vector3.Zero);
        Assert.Equal(1, session.Count);
        session.Step(Vector3.Zero);
        Assert.Equal(6, session.Count);
        Assert.Equal(5, session.Live.Count(child => child.Generation == 1));
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.Equal(31, session.Count);
        Assert.All(session.Live, child => Assert.InRange(child.Generation, 0, 2));
        session.Step(Vector3.Zero);
        Assert.True(p.Smoke);
        for (int i = 0; i < 150; i++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void TrailGrowsSlowsAndOutlivesItsEmitter()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        var p = session.Trail(new(100))!;
        float speed = p.Velocity.Length();
        session.Step(Vector3.Zero);
        Assert.Equal(50f / 210, p.Size, 5);
        Assert.Equal(speed * .98f, p.Velocity.Length(), 6);
        Assert.Equal(Rac1ParticleSession.Blend.Additive, p.Blending);
        for (int i = 0; i < 43; i++) session.Step(Vector3.Zero);
        Assert.True(p.Size > .7f);
        Assert.Equal(1, session.Count);
        session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void PoolExhaustionDoesNotReplaceLiveRecordsOrGrowWithoutBound()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0), capacity: 1);
        var first = session.Flame(new(100), Vector3.Zero, 12, false)!;
        Assert.Null(session.Trail(new(100)));
        Assert.Equal(1, session.Count);
        Assert.True(session.IsLive(first));
        session.Remove(first);
        var replacement = session.Trail(new(100))!;
        Assert.False(session.IsLive(first));
        Assert.Equal(first.Slot, replacement.Slot);
        Assert.NotEqual(first.Identity, replacement.Identity);
    }

    [Fact]
    public void NativeFlameBoundsAreExplicitlyDisabledOnlyForSyntheticFixtures()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        session.Flame(new(2000), Vector3.Zero, 12, false);
        session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
        session.Flame(new(2000), Vector3.Zero, 12, false);
        session.Step(Vector3.Zero, enforceLevelBounds: false);
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void OnlyTheFiveLargeBombFragmentsCarryTrailsAndTheSprayUsesItsImpactNormal()
    {
        var pieces = Rac1BombExplosion.Fragments(new(100), Rac1PickupRandom.FromSeed(121), Vector3.UnitX, Vector3.Zero);
        Assert.Equal(15, pieces.Count);
        Assert.Equal(5, pieces.Count(p => p.Trail));
        Assert.All(pieces, p => Assert.True(p.Velocity.X >= 0));
        Assert.All(pieces, p => Assert.Equal(Vector3.Zero, p.Angles));
    }

    [Fact]
    public void TrackedFlameEmitsTwoSmallSecondaryMotesOnceAndTheyFinishAllThreePhases()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        var flame = session.Flame(new(100), Vector3.UnitX / 60, 12, false)!;
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        session.FlameSecondary(flame); session.FlameSecondary(flame);
        var motes = session.Live.Where(p => p.Type == 2).ToArray();
        Assert.Equal(2, motes.Length);
        Assert.All(motes, p => Assert.Equal((byte)0x44, p.DepthRange));
        session.Step(Vector3.Zero);
        Assert.All(motes, p => { Assert.Equal(.2f, p.Size); Assert.Equal(0u, p.Color >> 24); });
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.All(motes, p => Assert.Equal(.2f, p.Size));
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.All(motes, p => Assert.Equal(.1f, p.Size));
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
        Assert.Equal(Vector3.Zero, Rac1ParticleSession.QuantizeDirection(Vector3.Zero));
        var q = Rac1ParticleSession.QuantizeDirection(new(.2f, -.1f, .04f));
        Assert.InRange(Vector3.Distance(q, new(.2f, -.1f, .04f)), 0, .006f);
    }

    [Fact]
    public void GlowingPuffAdvancesThroughTenTexturesAndFadesInItsLastSixFrames()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), new(() => 0));
        var puff = session.Glow(new(100), Vector3.UnitX / 60, 0x60000000, 0x60000000, 20, 210000)!;
        Assert.Equal(80, puff.Texture);
        for (int i = 0; i < 10; i++) session.Step(Vector3.Zero);
        Assert.Equal(85, puff.Texture); Assert.Equal(.5f, puff.Size);
        Assert.Equal(96u, puff.Color >> 24);
        for (int i = 0; i < 5; i++) session.Step(Vector3.Zero);
        Assert.Equal(80u, puff.Color >> 24);
        for (int i = 0; i < 4; i++) session.Step(Vector3.Zero);
        Assert.Equal(89, puff.Texture); Assert.Equal(16u, puff.Color >> 24);
        session.Step(Vector3.Zero); Assert.Equal(0, session.Count);
    }

    [Fact]
    public void BallisticSparkShedsChildrenButTheyDoNotRecurseAndEverythingExpires()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), Rac1PickupRandom.FromSeed(121));
        var spark = session.Spark(new(100), Vector3.UnitZ / 60, 0x4f007fff, 0x1f00007f, 60, true, 40000)!;
        session.Step(Vector3.Zero);
        Assert.Equal(2, session.Count);
        Assert.True(spark.Velocity.Z < 1f / 60);
        int maximum = 0;
        for (int i = 0; i < 200; i++) { session.Step(Vector3.Zero); maximum = Math.Max(maximum, session.Count); }
        Assert.InRange(maximum, 1, 31); Assert.Equal(0, session.Count);
    }

    [Fact]
    public void GroundMineUsesSteamFamiliesAndTheNativeCameraFade()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), Rac1PickupRandom.FromSeed(121));
        session.MineGroundExplosion(new(100), new(110, 100, 500));
        Assert.Equal(150, session.Count); // Native planar camera distance, independent of height.
        Assert.All(session.Live, p => Assert.Equal(16, p.Type));
        Assert.Contains(session.Live, p => p.Texture == 161);
        Assert.Contains(session.Live, p => p.Texture == 162);
        for (int i = 0; i < 500; i++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
        var near = session.Steam(new(100), Vector3.Zero, 0x7fffffff, 0, 100, 3, 200000)!;
        session.Step(new(101, 100, 100)); Assert.False(session.IsLive(near));
    }

    [Fact]
    public void GenericWeaponRecipesKeepTheirDistinctParticleAndDebrisCounts()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), Rac1PickupRandom.FromSeed(121));
        var rocket = Rac1WeaponExplosion.ForWeapon(Rac1WeaponId.Devastator);
        session.WeaponExplosion(rocket, new(100), new(110, 100, 100));
        Assert.Equal(10, session.Live.Count(p => p.Type == 15));
        Assert.Equal(6, session.Live.Count(p => p.Type == 11));
        Assert.Equal(9, session.Live.Count(p => p.Type == 8));
        Assert.Single(rocket.Fragments(new(100), new(110, 100, 100), Rac1PickupRandom.FromSeed(121)));
        Assert.Empty(rocket.Fragments(new(100), Vector3.Zero, Rac1PickupRandom.FromSeed(121)));
        var doom = Rac1WeaponExplosion.ForWeapon(Rac1WeaponId.GloveOfDoom);
        Assert.Equal(5, doom.Fragments(new(100), new(110, 100, 100), Rac1PickupRandom.FromSeed(121)).Count);
        Assert.Equal(2, doom.Bursts(5).Count); Assert.Equal(4, doom.Bursts(10).Count);
        Assert.Equal(16, Rac1WeaponExplosion.ForWeapon(Rac1WeaponId.VisibombGun).Puffs);
        Assert.Equal(.4f, Rac1WeaponExplosion.ForWeapon(Rac1WeaponId.DroneDevice).ShellSize);
        for (int i = 0; i < 400; i++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void SustainedEmissionAndRepeatedExplosionsStayWithinTheSharedPoolAndDrain()
    {
        var session = new Rac1ParticleSession(SyntheticAssets(), Rac1PickupRandom.FromSeed(121));
        var recipe = Rac1WeaponExplosion.ForWeapon(Rac1WeaponId.Devastator);
        for (int tick = 0; tick < 600; tick++)
        {
            session.Pyrocitor(new(100), Vector3.UnitX, 12);
            if (tick % 10 == 0) session.WeaponExplosion(recipe, new(100), Vector3.Zero);
            session.Step(Vector3.Zero);
            Assert.InRange(session.Count, 1, 2048);
            Assert.All(session.Live, p => { Assert.True(float.IsFinite(p.Size)); Assert.True(float.IsFinite(p.Position.Z)); });
        }
        for (int tick = 0; tick < 500; tick++) session.Step(Vector3.Zero);
        Assert.Equal(0, session.Count);
    }

    [SkippableFact]
    public void NativeFlameParametersAndOrdinaryParticleFamiliesArePinned()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int at) => BinaryPrimitives.ReadInt32LittleEndian(read(at, 4));
        Assert.Equal(0x3e800000, Word(0x1601b8)); // .25 start diameter.
        Assert.Equal(0x3fa00000, Word(0x1601bc)); // 1.25 end diameter.
        Assert.Equal(20, Word(0x1601c0)); Assert.Equal(30, Word(0x1601c4));
        Assert.Equal(0x3f800000, Word(0x1601d8)); Assert.Equal(0x40400000, Word(0x1601dc));
        Assert.Equal(25, Word(0x1601e0)); Assert.Equal(35, Word(0x1601e4));
        Assert.Equal(0x42400000, Word(0x1601e8)); // Acceleration48.
        Assert.Equal(2, Word(0x161518)); Assert.Equal(2, Word(0x16151c));
        Assert.Equal(0x2404000b, Word(0x26ab20)); // Ordinary splitting explosion, distinct from type34 dust.
        Assert.Equal(0x24040004, Word(0x26999c));
    }
}
