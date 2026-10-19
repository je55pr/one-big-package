using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1BombProjectileTests
{
    [Fact]
    public void ParticleTableUsesPaletteFirstAndPixelsThirdWithAssetLocalPaletteAndGsClutOrder()
    {
        var core = ParticleFixture();
        var texture = Assert.Single(Rac1ParticleAssets.ReadTextures(core, new HashSet<int> { 0 }));
        Assert.Equal(2, texture.Width);
        Assert.Equal(new byte[] { 0, 16, 8, 24 }, Enumerable.Range(0, 4).Select(i => texture.Rgba[i * 4]).ToArray());
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(128, texture.Rgba[i * 4 + 3]));
        Assert.Equal(0, Rac1ParticleAssets.InitialTexture(core, 0));
    }

    [Fact]
    public void ParticleLookupAndPayloadRejectOutOfRangeReferences()
    {
        var core = ParticleFixture();
        Assert.Throws<InvalidDataException>(() => Rac1ParticleAssets.InitialTexture(core, 1));
        BinaryPrimitives.WriteInt32LittleEndian(core.Index.AsSpan(0x110), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => Rac1ParticleAssets.InitialTexture(core, 0));
        BinaryPrimitives.WriteInt32LittleEndian(core.Index.AsSpan(0x188), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => Rac1ParticleAssets.ReadTextures(core, new HashSet<int> { 0 }));
    }

    private static Rac1LevelCore.Core ParticleFixture()
    {
        byte[] index = new byte[512], assets = new byte[2048];
        void Word(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(offset), value);
        Word(0x14, 32); Word(0x60, 64); Word(0x8c, assets.Length);
        Word(0x50, 1); Word(0x54, 0x180); Word(0x6c, 0x100);
        Word(0x100, 1); Word(0x108, 32); Word(0x10c, 1); Word(0x110, 32);
        Word(0x180, 0); Word(0x188, 1024); Word(0x18c, 2);
        for (int color = 0; color < 256; color++)
        {
            assets[color * 4] = (byte)color;
            assets[color * 4 + 3] = 64;
        }
        new byte[] { 0, 8, 16, 24 }.CopyTo(assets, 1024);
        return new(index, assets, [], Rac1LevelCore.ReadHeader(index));
    }

    [Fact]
    public void UntargetedThrowHasWitnessedUpwardStepAndFallsInAnArc()
    {
        var origin = new Vector3(155.40013f, 121.167595f, 29.975195f);
        float yaw = MathF.Atan2(.1269561946f, .0628614724f);
        Vector3 direction = new(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var launch = Rac1BombProjectileSession.LaunchVelocity(origin, origin + direction * 8.5f, yaw);
        Assert.InRange(launch.X, .06285f, .06288f);
        Assert.InRange(launch.Y, .12694f, .12698f);
        // Exact-range rounding can admit the source's 8.49-unit clamp.
        Assert.InRange(launch.Z, .09155f, .09168f);
        var velocity = new Vector3(.0628614724f, .1269561946f, .0916665196f);
        var bomb = new Rac1BombProjectileSession(origin, velocity);
        Assert.Equal(origin + velocity, bomb.AdvanceFlight());
        Assert.Equal(velocity.Z - Rac1BombProjectileSession.GravityStep, bomb.Velocity.Z);
        for (int tick = 1; tick < 31; tick++) bomb.AdvanceFlight();
        Assert.True(bomb.Position.Z > origin.Z + 1.3f);
        for (int tick = 31; tick < 65; tick++) bomb.AdvanceFlight();
        Assert.True(bomb.Position.Z < origin.Z);
        Assert.InRange(new Vector2(bomb.Position.X - origin.X, bomb.Position.Y - origin.Y).Length(), 9.1f, 9.3f);
    }

    [Fact]
    public void AimSolvesLowerTargetAndClampsExcessiveElevationAndRange()
    {
        var origin = new Vector3(100, 100, 20);
        var lower = Rac1BombProjectileSession.LaunchVelocity(origin, origin + new Vector3(4, 0, -1), 0);
        float time = 4 / Rac1BombProjectileSession.HorizontalSpeed;
        Assert.InRange(lower.Z * time - .5f * Rac1BombProjectileSession.GravityStep * time * time, -1.0001f, -.9999f);
        var high = Rac1BombProjectileSession.LaunchVelocity(origin, origin + new Vector3(20, 0, 10), 0);
        Assert.InRange(high.Z, .09166f, .09167f);
        Assert.Equal(Rac1BombProjectileSession.HorizontalSpeed, high.X);
        var behind = Rac1BombProjectileSession.LaunchVelocity(origin, origin - Vector3.UnitX * 4, 0);
        Assert.True(behind.X > 0);
        Assert.Equal(0, behind.Y);
    }

    [Fact]
    public void BlastRemainsStationaryAndExpandsForSixteenContactQueries()
    {
        var bomb = new Rac1BombProjectileSession(Vector3.One, Vector3.UnitX);
        var impact = new Vector3(4, 5, 6);
        bomb.Impact(impact);
        for (int age = 0; age <= 15; age++)
        {
            Assert.Equal(2, bomb.NativeState);
            Assert.Equal(impact, bomb.Position);
            Assert.Equal(.5f + 2 * age / 15f, bomb.BlastRadius);
            bomb.AdvanceBlast();
        }
        Assert.True(bomb.Finished);
    }

    [Fact]
    public void BurstGrowsLinearlyAndFadesOnlyInItsLastHalf()
    {
        var burst = Rac1BombExplosion.Bursts(false)[0];
        Assert.Equal(0, burst.Scale);
        for (int tick = 0; tick < 10; tick++) burst.Step();
        Assert.Equal(2, burst.Scale);
        Assert.Equal(.375f, burst.Alpha);
        for (int tick = 10; tick < 15; tick++) burst.Step();
        Assert.Equal(.1875f, burst.Alpha);
        for (int tick = 15; tick < 20; tick++) burst.Step();
        Assert.True(burst.Finished);
        Assert.Equal(0, burst.Alpha);
    }

    [SkippableFact]
    public void RetailGloveSocketThrowAndImpactRecipesMatchExecutable()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        Assert.Equal(6, Word(0x179ac0 + 10 * 0x4c + 0xc));
        Assert.Equal(192, Word(0x179ac0 + 10 * 0x4c + 0x10));
        Assert.Equal(0x2404002c, Word(0x226308)); // Body sequence44.
        Assert.Equal(0x24050007, Word(0x226310)); // Seven-tick blend.
        Assert.Equal(0x24040009, Word(0x2262f8)); // Entry frame9.
        Assert.Equal(0x3c0141a8, Word(0x231c24)); // Finish authored time21.
        Assert.Equal(0x3c014108, Word(0x2c25f4)); // Ordinary horizontal speed8.5.
        Assert.Equal(0x3c014130, Word(0x2ae038)); // Gravity11.
        Assert.Equal(0x0c07bf1c, Word(0x2ad370)); // Body-to-launch world query.
        Assert.Equal(1192, Word(0x1ea6c0));
        Assert.Equal(0x2acad8, Word(0x1ea6c4)); // Expanding burst update.
        Assert.Equal(0x2404007a, Word(0x2af484)); // Native fragment class122.
        Assert.Equal(0x2404000f, Word(0x2af358)); // Blast age bound15.
        Assert.Equal(unchecked((int)0x8e650064), Word(0x2447f8)); // Independent Part texture pixel base.
        Assert.Equal(unchecked((int)0x8e64006c), Word(0x244804)); // Particle lookup descriptor.
        Assert.Equal(0x24040022, Word(0x26f0ac)); // Sparks allocate pool type34.
    }

    [SkippableFact]
    public void RetailAssetsDecodeGloveAndBodyAttachmentWithoutRootCalibration()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var asset = Rac1BombGloveAssets.Load(path!);
        Assert.Equal(1140, asset.VertexCount);
        Assert.Equal(1200, asset.TriangleCount);
        Assert.Equal(11, asset.Clips[0].Frames.Length);
        Assert.Single(asset.Clips[1].Frames);
        Assert.All(asset.Clips[0].Durations, duration => Assert.Equal(1d / 30, duration));
        Assert.NotEmpty(asset.Textures);
        Assert.Equal(0, asset.ParticleTexturesByPoolType[4].TextureId);
        Assert.Equal(33, asset.ParticleTexturesByPoolType[34].TextureId);
        Assert.All(asset.ParticleTexturesByPoolType.Values, texture => Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length));
        Assert.All(asset.Clips.Values, clip => Assert.All(clip.Frames, frame => Assert.All(frame, value => Assert.True(double.IsFinite(value)))));
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(path!, "ratchet");
        var clip = avatar.AnimationClips.Single(clip => clip.Id == "bomb-glove-throw");
        Assert.Equal(27, clip.FrameCount);
        Assert.True(clip.AttachmentFrames!.ContainsKey(Rac1BombProjectileSession.GloveSocketKey));
        Assert.NotEqual(clip.AttachmentFrames[Rac1BombProjectileSession.GloveSocketKey][9],
            clip.AttachmentFrames[Rac1WrenchAttachment.SocketKey][9]);
    }
}
