using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac2ParticleTests
{
    internal static readonly ObpSourceRef Source = new(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, "LEVEL21", "moby", "diagnostic");
    internal static void Put(byte[] b, int at, int n) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), n);
    internal static void Put(byte[] b, int at, float n) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), n);
    internal static byte[] Parameters()
    {
        // Synthetic branch fixture, never presented as a second authored effect.
        byte[] p = new byte[80];
        Put(p, 8, .125f); Put(p, 0x10, 4); Put(p, 0x14, 18); Put(p, 0x18, 42); Put(p, 0x1c, 128);
        Put(p, 0x20, -5); Put(p, 0x24, 20); Put(p, 0x28, 55); Put(p, 0x2c, .4f); Put(p, 0x30, 1.25f); Put(p, 0x40, 62);
        return p;
    }
    internal static Rac2MuseumParticleEmitter Owner(byte[]? bytes = null) => new(Source, Rac2MuseumParticleParameters.Parse(bytes ?? Parameters()), Vector3.Zero);
    internal static byte[] DefinitionsBytes()
    {
        byte[] bytes = new byte[0x1d0 + 208];
        Put(bytes, 0, 111); Put(bytes, 4, 109); Put(bytes, 8, 0x1d0); Put(bytes, 12, 208);
        for (int i = 0; i < 104; i++) Put(bytes, 16 + i * 4, 0x1d0);
        Put(bytes, 16 + 12 * 4, 0x1d0 + 3); bytes[0x1d0 + 3] = 23;
        Put(bytes, 16 + 22 * 4, 0x1d0 + 1); bytes[0x1d0 + 1] = 41;
        Put(bytes, 16 + 23 * 4, 0x1d0 + 2); bytes[0x1d0 + 11] = 47;
        Put(bytes, 16 + 62 * 4, 0x1d0 + 4); bytes[0x1d0 + 4] = 44;
        Put(bytes, 16 + 85 * 4, 0x1d0 + 5); bytes[0x1d0 + 5] = 39;
        return bytes;
    }
    internal static Rac2ParticleAssets Assets() => new(Source, Rac2ParticleDefinitions.Parse(DefinitionsBytes(), 109),
        Enumerable.Range(0, 109).ToDictionary(i => i, i => new RuntimeTexture(Rac2ParticleAssets.AssetKind, i, 1, 1, [255, 255, 255, 255])))
    { NativeLayouts = Enumerable.Range(0, 109).ToDictionary(i => i, _ => new Rac2PartTextureLayout(0, 1024, 32)) };

    [Fact]
    public void ParametersAreBoundedImmutableAndRejectUnsupportedNativeContextsAtAdmission()
    {
        byte[] b = Parameters(); var p = Rac2MuseumParticleParameters.Parse(b); Put(b, 0x10, 0);
        Assert.Equal(4, p.Lifespan); Assert.Throws<InvalidDataException>(() => Rac2MuseumParticleParameters.Parse(b));
        b = p.CopyNativeParameters(); Put(b, 0, float.NaN);
        Assert.Throws<InvalidDataException>(() => Rac2MuseumParticleParameters.Parse(b));
        b = p.CopyNativeParameters(); Put(b, 0x38, 2);
        Assert.Throws<NotSupportedException>(() => Owner(b));
        Assert.Throws<ArgumentException>(() => new Rac2MuseumParticleEmitter(Source with { Game = ObpSourceGame.Rac1 }, p, Vector3.Zero));
        Assert.Throws<ArgumentException>(() => new Rac2MuseumParticleEmitter(Source, p, new(float.PositiveInfinity, 0, 0)));
    }

    [Fact]
    public void MenuUsesHundredthFloatStepsIntegerClampsAndShortWrapBeforeClamp()
    {
        var p = Rac2MuseumParticleParameters.Parse(Parameters());
        var velocity = new Rac2MuseumEffectControls.Parameter("Velocity X", 0, Rac2MuseumEffectControls.Storage.Float32, -1000, 1000, 0);
        p = p.Edit(velocity, 5); Assert.Equal(.049999997f, p.Velocity.X);
        var life = new Rac2MuseumEffectControls.Parameter("Lifespan", 0x10, Rac2MuseumEffectControls.Storage.Int32, 1, 1000, 0);
        Assert.Equal(1, p.Edit(life, -5).Lifespan); Assert.Equal(1000, p.Edit(life, 2000).Lifespan);
        var timer = new Rac2MuseumEffectControls.Parameter("Low Spwn Timer", 0x4c, Rac2MuseumEffectControls.Storage.Int16, 0, 1000, 0);
        Assert.Equal(0, p.Edit(timer, 32768).LowSpawnTimer);
        Assert.Throws<ArgumentException>(() => p.Edit(velocity with { PVarOffset = 0xc }, 1));
        Assert.Throws<ArgumentException>(() => p.Edit(life with { Kind = Rac2MuseumEffectControls.Storage.Float32 }, 1));
    }

    [Fact]
    public void ConstructorAndMuseumCallerOwnDifferentTextureAndDepthDefaults()
    {
        var rng = Rac2ParticleRandom.FromSeed(1234); var session = new Rac2ParticleSession(Assets(), rng);
        var common = session.Spawn85(Source, new(Vector3.Zero, Vector3.Zero, 100, 0x123456, 0, 0, 127, 0, 127, 0))!;
        Assert.Equal(127, common.Total); Assert.Equal(127, common.Remaining); Assert.Equal(39, common.Texture); Assert.Equal(0x44, common.DepthRange);
        Assert.Equal(1, rng.SamplesConsumed);
        var emission = session.Emit(Owner()); var editor = emission.Particle!;
        Assert.Equal(Rac2ParticleSession.EmissionStatus.Emitted, emission.Status);
        Assert.Equal(44, editor.Texture); Assert.Equal(0xff, editor.DepthRange); Assert.Equal(0x48, editor.Blend);
        Assert.Equal("44", editor.TextureSource.OriginalId); Assert.Equal(Rac2ParticleAssets.AssetKind, editor.TextureSource.AssetKind);
        Assert.Equal(4, rng.SamplesConsumed); Assert.True(Rac2ParticleSession.SupportsType(85)); Assert.False(Rac2ParticleSession.SupportsType(97));
    }

    [Fact]
    public void CallbackMovesBeforeGravityWrapsSpinAndExpiresBeforeItsLastMovement()
    {
        var session = new Rac2ParticleSession(Assets(), Rac2ParticleRandom.FromSeed(0));
        var p = session.Spawn85(Source, new(new(1, 2, 3), new(.5f, -.25f, 1), 3, 0xabcdef, -5, 127, 0, 10, 4, .25f))!;
        byte angle = p.Angle;
        session.Step(); Assert.Equal(new(1.5f, 1.75f, 4), p.Position); Assert.Equal(.75f, p.Velocity.Z);
        Assert.Equal(85u, p.Color >> 24); Assert.Equal(unchecked((byte)(angle - 5)), p.Angle); Assert.Equal(8, p.NativeSize);
        session.Step(); Assert.Equal(43u, p.Color >> 24); Assert.Equal(new(2, 1.5f, 4.75f), p.Position);
        session.Step(); Assert.False(session.IsLive(p)); Assert.Equal(new(2, 1.5f, 4.75f), p.Position); Assert.Equal(6, p.NativeSize);
        session.Remove(p); Assert.Equal(0, session.Count);
    }

    [Fact]
    public void ZeroOpacityQuotientDoesNotTweenAndNativeSizeUsesChopAccumulation()
    {
        byte[] bytes = Parameters(); Put(bytes, 0x10, 40); Put(bytes, 0x24, 5); Put(bytes, 0x28, 0);
        var session = new Rac2ParticleSession(Assets(), Rac2ParticleRandom.FromSeed(1234));
        var p = session.Emit(Owner(bytes)).Particle!;
        Assert.Equal(32, p.Angle); Assert.Equal(84000, p.NativeSize);
        session.Step(); Assert.Equal(88462.5f, p.NativeSize); Assert.Equal(5u, p.Color >> 24);
        for (int i = 1; i < 40; i++) session.Step(); Assert.Empty(session.Live);
    }

    [Fact]
    public void CountdownReadsTheOldValueAndFullPoolSkipsOnlyConstructorRng()
    {
        byte[] bytes = Parameters(); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4c), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4e), 2);
        var owner = Owner(bytes); var rng = Rac2ParticleRandom.FromSeed(0); var session = new Rac2ParticleSession(Assets(), rng, 1);
        Assert.Equal(Rac2ParticleSession.EmissionStatus.Countdown, session.Emit(owner).Status); Assert.Equal(1, owner.Countdown);
        Assert.Equal(Rac2ParticleSession.EmissionStatus.Countdown, session.Emit(owner).Status); Assert.Equal(0, owner.Countdown);
        Assert.Equal(Rac2ParticleSession.EmissionStatus.Emitted, session.Emit(owner).Status); Assert.Equal(3, rng.SamplesConsumed);
        session.Emit(owner); session.Emit(owner);
        Assert.Equal(Rac2ParticleSession.EmissionStatus.PoolFull, session.Emit(owner).Status); Assert.Equal(4, rng.SamplesConsumed);
        session.Clear(); Assert.Equal(0, session.Count); Assert.Empty(session.Live);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac2ParticleSession(Assets(), rng, 2049));
    }

    [Fact]
    public void OwnerEditsOnlyAffectFutureParticlesAndStoppedOwnerDrainsCopiedRecords()
    {
        var owner = Owner(); var session = new Rac2ParticleSession(Assets(), Rac2ParticleRandom.FromSeed(0));
        var p = session.Emit(owner).Particle!;
        byte[] changed = Parameters(); Put(changed, 8, 7f); Put(changed, 0x40, 12);
        owner.ApplyNativeFrame(Rac2MuseumParticleParameters.Parse(changed), new(10, 20, 30));
        Assert.Equal(.125f, p.Velocity.Z); Assert.Equal(Vector3.Zero, p.Position); Assert.Equal(44, p.Texture);
        var next = session.Emit(owner).Particle!; Assert.Equal(23, next.Texture); Assert.Equal(7, next.Velocity.Z);
        owner.StopEmission(); Assert.Equal(Rac2ParticleSession.EmissionStatus.Stopped, session.Emit(owner).Status);
        var output = new List<Rac2ParticleSession.SpriteSnapshot>(); session.CaptureSprites(output);
        Assert.Equal(2, output.Count); Assert.Equal(owner.Source, output[0].EmitterSource);
        session.Remove(p); var reused = session.Spawn85(Source, new(Vector3.Zero, Vector3.Zero, 1, 0, 0, 0, 0, 0, 0, 0))!;
        Assert.Equal(p.Slot, reused.Slot); Assert.NotEqual(p.Identity, reused.Identity); session.Remove(p); Assert.True(session.IsLive(reused));
        for (int i = 0; i < 4; i++) session.Step(); session.CaptureSprites(output); Assert.Empty(output);
        Assert.Throws<InvalidOperationException>(() => session.Spawn85(Source with { LevelId = "LEVEL01" }, default));
    }

    [Fact]
    public void NativeTimerClampsPersistOnlyAtInitializationAndEmission()
    {
        byte[] bytes = Parameters(); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4c), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4e), 2);
        var owner = Owner(bytes); var session = new Rac2ParticleSession(Assets(), Rac2ParticleRandom.FromSeed(0));
        session.Emit(owner);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4e), 1);
        owner.ApplyNativeFrame(Rac2MuseumParticleParameters.Parse(bytes), owner.Position);
        session.Emit(owner); Assert.Equal(2, owner.Parameters.LowSpawnTimer);
        session.Emit(owner); Assert.Equal(1, owner.Parameters.LowSpawnTimer);
    }

    [Fact]
    public void DefinitionFramesIndexTheSharedBlobAndNativeZeroOffsetSelectsItsFirstByte()
    {
        byte[] bytes = DefinitionsBytes(); var defs = Rac2ParticleDefinitions.Parse(bytes, 109);
        Assert.Equal(47, defs.Texture(22, 10)); Assert.Equal(0, defs.Texture(104, 0));
        Assert.Throws<InvalidDataException>(() => defs.Texture(22, 208)); Assert.Throws<InvalidDataException>(() => defs.Texture(-1, 0));
        Put(bytes, 16, int.MinValue); Assert.Throws<InvalidDataException>(() => Rac2ParticleDefinitions.Parse(bytes, 109));
        bytes = DefinitionsBytes(); bytes[^1] = 109; Assert.Throws<InvalidDataException>(() => Rac2ParticleDefinitions.Parse(bytes, 109));
        bytes = DefinitionsBytes(); Put(bytes, 8, 0x10); Assert.Throws<InvalidDataException>(() => Rac2ParticleDefinitions.Parse(bytes, 109));
    }

    [Fact]
    public void SpriteUsesNativeDepthFadeProjectionSpinAndMaterialBits()
    {
        var session = new Rac2ParticleSession(Assets(), Rac2ParticleRandom.FromSeed(0));
        var p = session.Spawn85(Source, new(Vector3.Zero, Vector3.Zero, 10, 0x123456, 0, 64, 64, 420000, 420000, 0))!;
        var draw = new Rac2ParticleDraw(Enumerable.Repeat(1f, 256).ToArray(), 0x8000000401304000);
        Rac2ParticleDraw.View view = new(new(0, 0, -2), Vector3.UnitZ, 1f / 420000, int.MaxValue);
        var sprite = draw.Build(p, view, new(0, 1024, 32), 0x100000)!.Value;
        Assert.Equal(64u, sprite.Color >> 24); Assert.InRange(sprite.ProjectedAxes.X, .3999999f, .4f);
        Assert.Equal(0x8002000555305004ul, sprite.NativeTex0); Assert.Equal(64, sprite.CopyNativeDescriptor().Length);
        Assert.Equal(16u, draw.Build(p, view with { Eye = new(0, 0, -1.25f) }, new(0, 1024, 32), 0)!.Value.Color >> 24);
        Assert.Null(draw.Build(p, view with { Eye = new(0, 0, -1) }, new(0, 1024, 32), 0));
        Assert.Null(draw.Build(p, view with { Eye = new(0, 0, -128) }, new(0, 1024, 32), 0));
        Assert.Throws<ArgumentException>(() => new Rac2ParticleDraw(new float[255], 0));
        Assert.Equal((0xa4000u, 0xa8005u), new Rac2PartTextureLayout(0x9400, 0x9800, 32).Bind(0x1000));
    }

    private static Rac2LevelCore.Core PartCore()
    {
        byte[] index = new byte[0x400], assets = new byte[0x600];
        Put(index, 0x64, 0x100); Put(index, 0x6c, 0x100);
        Put(index, 0xc0, 0); Put(index, 0xc8, 1024); Put(index, 0xcc, 1);
        byte[] definitions = DefinitionsBytes(); Put(definitions, 4, 1); definitions.AsSpan(0x1d0).Clear(); definitions.CopyTo(index, 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(assets.AsSpan(0x104), 0x80804020); assets[0x500] = 1;
        var header = new Rac2LevelCore.CoreHeader(default, 0, 0, 0, 0, default, default, default,
            default, default, default, default, new(1, 0xc0), default, 0, 0, 0, 0, 0, 0, []);
        return new(null!, header, index, assets, [], []);
    }

    [Fact]
    public void PartTextureUsesItsOwnBankAndTheSharedPs2ClutAlphaCodec()
    {
        var decoded = Rac2ParticleAssets.Read(PartCore(), Source);
        Assert.Equal(new byte[] { 32, 64, 128, 255 }, decoded.Textures[0].Rgba);
        Assert.Equal(new Rac2PartTextureLayout(0, 1024, 1), decoded.NativeLayouts[0]);
    }

    [Theory]
    [InlineData(0xc0, -1)]
    [InlineData(0xc8, int.MaxValue)]
    [InlineData(0xcc, 3)]
    [InlineData(0x64, int.MaxValue)]
    [InlineData(0x6c, -1)]
    public void PartTextureRejectsOutOfBankRangesAndUnadmittedEncodings(int at, int value)
    {
        var core = PartCore(); Put(core.Index, at, value);
        Assert.Throws<InvalidDataException>(() => Rac2ParticleAssets.Read(core, Source));
        core = PartCore(); Put(core.Index, 0xc4, 1);
        Assert.Throws<NotSupportedException>(() => Rac2ParticleAssets.Read(core, Source));
        Assert.Throws<ArgumentException>(() => Rac2ParticleAssets.Read(PartCore(), Source with { Game = ObpSourceGame.Rac3 }));
    }
}
