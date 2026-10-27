using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac3ParticleTests
{
    internal static readonly ObpSourceRef Source = new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "TABLE1");
    internal static readonly Rac3ParticleOwner Owner = new(Source with { AssetKind = "moby", OriginalId = "9" }, 1);
    internal static readonly Vector4 Muzzle = new(2, 3, 4, 1), End = new(18, 11, 8, 1);
    internal static void Put(byte[] b, int at, int n) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), n);
    internal static byte[] DefinitionsBytes()
    {
        byte[] b = new byte[0x290]; Put(b, 0, 129); Put(b, 4, 65); Put(b, 8, 0x220); Put(b, 12, 112);
        Put(b, 16 + 97 * 4, 0x223); b[0x223] = 12;
        Put(b, 16 + 104 * 4, 0x224);
        for (int i = 0; i < 4; i++) b[0x224 + i] = (byte)(53 + i);
        return b;
    }
    internal static Rac3ParticleAssets Assets() => new(Source, Rac3ParticleDefinitions.Parse(DefinitionsBytes(), 65),
        Enumerable.Range(0, 65).ToDictionary(i => i, i => new RuntimeTexture(Rac3ParticleAssets.AssetKind, i, 1, 1, [255, 255, 255, 255])))
    { NativeLayouts = Enumerable.Range(0, 65).ToDictionary(i => i, _ => new Rac3PartTextureLayout(0, 1024, 32)) };

    [Fact]
    public void UyaConstructorRetainsRequestedLifeAndCopiesVectorW()
    {
        var session = new Rac3ParticleSession(Assets(), Rac3ParticleRandom.FromSeed(1234));
        var p = session.Spawn97(Owner, new(Muzzle, Vector3.Zero, 100, 0x123456, 0, 0, 127, 0, 127, 0))!;
        Assert.Equal(100, p.Total); Assert.Equal(100, p.Remaining); Assert.Equal(1, p.PositionW);
        Assert.Equal(12, p.Texture); Assert.Equal(0x48, p.Blend); Assert.Equal(0x44, p.DepthRange);
        byte[] b = p.CopyNativeRecord(); Assert.Equal(0x46de20u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(60)));
        Assert.Equal(1, b[52]); Assert.Equal(new byte[6], b[54..60]);
        for (int i = 0; i < 99; i++) session.Step(); Assert.Equal(99u, p.Color >> 24); Assert.True(session.IsLive(p));
        session.Step(); Assert.Empty(session.Live);
        Assert.True(Rac3ParticleSession.SupportsType(97)); Assert.False(Rac3ParticleSession.SupportsType(107));
    }

    [Fact]
    public void ActualChargeRecipeGoldenInputsUseUyasOwnRngAndReversedSpinRange()
    {
        var rng = Rac3ParticleRandom.FromSeed(1234); var session = new Rac3ParticleSession(Assets(), rng);
        Assert.Equal(new(2, 2), session.EmitShockCannonCharging(Owner, 30, Muzzle));
        var p = session.Live.First(); Assert.Equal(8, rng.SamplesConsumed);
        Assert.Equal(19, p.Total); Assert.Equal(11, p.Angle); Assert.Equal(55, p.Texture);
        Assert.Equal(0x0ae19e20u, p.Color); Assert.Equal(21000, p.NativeSize); Assert.Equal(new(2, 3, 4), p.Position);
        session.Step(); Assert.Equal(10, p.Angle); Assert.Equal(10u, p.Color >> 24); Assert.Equal(new(2, 3, 4), p.Position);
        Assert.Equal(1f / 3600, p.Velocity.Z);
        for (int i = 1; i < 20; i++) session.Step(); Assert.Empty(session.Live);
    }

    [Fact]
    public void ChargedShotGlowReusesType97WithFiveInterpolatedPointsAndOneMuzzlePoint()
    {
        var rng = Rac3ParticleRandom.FromSeed(1234); var session = new Rac3ParticleSession(Assets(), rng);
        Assert.Equal(new(6, 6), session.EmitShockCannonBeamGlow(Owner, Muzzle, End)); Assert.Equal(29, rng.SamplesConsumed);
        var points = session.Live.ToArray();
        foreach (var p in points[..5]) { Assert.InRange(p.Position.X, 2, 18); Assert.Equal(168000, p.NativeSize); }
        Assert.Equal(new(2, 3, 4), points[5].Position); Assert.Equal(63000.00390625f, points[5].NativeSize);
        Assert.All(points, p => { Assert.Equal(0x50804020u, p.Color); Assert.Equal(Rac3ParticleRecipe.ShockCannonBeamGlow, p.Recipe); Assert.InRange(p.Texture, 53, 56); });
    }

    [Fact]
    public void ExpiryPrecedesMotionAndGravityAndSpinUseSourceOperationOrder()
    {
        var session = new Rac3ParticleSession(Assets(), Rac3ParticleRandom.FromSeed(0));
        var p = session.Spawn97(Owner, new(new(1, 2, 3, 7), new(.5f, -.25f, 1), 3, 0xabcdef, -5, 127, 0, 10, 4, .25f))!;
        byte angle = p.Angle;
        session.Step(); Assert.Equal(new(1.5f, 1.75f, 4), p.Position); Assert.Equal(.75f, p.Velocity.Z);
        Assert.Equal(85u, p.Color >> 24); Assert.Equal(unchecked((byte)(angle - 5)), p.Angle); Assert.Equal(8, p.NativeSize);
        session.Step(); Assert.Equal(new(2, 1.5f, 4.75f), p.Position); Assert.Equal(43u, p.Color >> 24);
        session.Step(); Assert.False(session.IsLive(p)); Assert.Equal(new(2, 1.5f, 4.75f), p.Position); Assert.Equal(7, p.PositionW);
    }

    [Fact]
    public void FullPoolSkipsConstructorAndPostTextureRngButRetainsBothRecipeDraws()
    {
        var rng = Rac3ParticleRandom.FromSeed(0); var session = new Rac3ParticleSession(Assets(), rng, 1);
        var first = session.EmitShockCannonCharging(Owner, 30, Muzzle);
        Assert.Equal(new(2, 1), first); Assert.Equal(6, rng.SamplesConsumed);
        Assert.Equal(new(6, 0), session.EmitShockCannonBeamGlow(Owner, Muzzle, End)); Assert.Equal(23, rng.SamplesConsumed);
        var old = session.Live.Single(); session.Remove(old);
        var next = session.Spawn97(Owner, new(Muzzle, Vector3.Zero, 1, 0, 0, 0, 0, 0, 0, 0))!;
        Assert.Equal(old.Slot, next.Slot); Assert.NotEqual(old.Identity, next.Identity); session.Remove(old); Assert.True(session.IsLive(next));
        session.Clear(); Assert.False(session.IsLive(next)); Assert.Equal(0, session.Count);
    }

    [Fact]
    public void ForeignSourcesInvalidInputsAndMovingContextFailBeforeMutation()
    {
        var rng = Rac3ParticleRandom.FromSeed(0); var session = new Rac3ParticleSession(Assets(), rng);
        Assert.Throws<InvalidOperationException>(() => session.EmitShockCannonCharging(Owner with { Source = Source with { Game = ObpSourceGame.Rac2 } }, 30, Muzzle));
        Assert.Throws<NotSupportedException>(() => session.EmitShockCannonCharging(Owner, 79, Muzzle));
        Assert.Throws<ArgumentException>(() => session.Spawn97(Owner, new(new(float.NaN, 0, 0, 1), Vector3.Zero, 1, 0, 0, 0, 0, 0, 0, 0)));
        var p = session.EmitShockCannonCharging(Owner, 30, Muzzle); long before = rng.SamplesConsumed;
        var b = session.Live.First().CopyNativeRecord();
        Assert.Throws<NotSupportedException>(() => session.Step(movingFrameActive: true));
        Assert.Throws<NotSupportedException>(() => session.Step(directionalGravityActive: true));
        Assert.Equal(b, session.Live.First().CopyNativeRecord()); Assert.Equal(before, rng.SamplesConsumed);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac3ParticleSession(Assets(), rng, 2049));
    }

    [Fact]
    public void EmitterConsumesCurrentFrameOnceWithoutAdvancingWeaponOrConsumingDamage()
    {
        var weapon = new Rac3ShockCannonSession(); var equipped = weapon.Equip();
        var emitter = new Rac3ShockCannonParticleEmitter(weapon, equipped, Source, new(0x1882, 9));
        var rng = Rac3ParticleRandom.FromSeed(1234); var particles = new Rac3ParticleSession(Assets(), rng);
        var frame = weapon.Advance(new(true)); int ammo = weapon.Ammo;
        Assert.Equal(2, emitter.Emit(frame, particles, Muzzle, End).Emitted);
        Assert.Equal(Rac3ShockCannonParticleEmitter.Status.AlreadyConsumed, emitter.Emit(frame, particles, Muzzle, End).Admission);
        Assert.Equal(8, rng.SamplesConsumed); Assert.Equal(ammo, weapon.Ammo);
        Assert.Empty(weapon.ResolveContacts(frame, [])); // The visual path has not consumed the independent damage fence.
        weapon.Advance(new(true)); Assert.Equal(Rac3ShockCannonParticleEmitter.Status.StaleOwner, emitter.Emit(frame, particles, Muzzle, End).Admission);
        var unequipped = weapon.Unequip();
        Assert.Equal(Rac3ShockCannonParticleEmitter.Status.StaleOwner, emitter.Emit(unequipped, particles, Muzzle, End).Admission);
        var output = new List<Rac3ParticleSession.SpriteSnapshot>(); particles.CaptureSprites(output);
        Assert.Equal(2, output.Count); Assert.All(output, p => Assert.Equal(Owner, p.Owner));
        emitter.StopEmission(); Assert.Equal(Rac3ShockCannonParticleEmitter.Status.Stopped, emitter.Emit(unequipped, particles, Muzzle, End).Admission);
        for (int i = 0; i < 20; i++) particles.Step(); particles.CaptureSprites(output); Assert.Empty(output);
    }

    [Fact]
    public void UnavailableTextureCanBeRetriedWithoutRngOrFrameConsumption()
    {
        var weapon = new Rac3ShockCannonSession(); var emitter = new Rac3ShockCannonParticleEmitter(weapon, weapon.Equip(), Source, new(0x1882, 9));
        var frame = weapon.Advance(new(true)); var rng = Rac3ParticleRandom.FromSeed(0);
        var incomplete = Assets() with { Textures = Assets().Textures.Where(kv => kv.Key != 56).ToDictionary() };
        Assert.Throws<InvalidDataException>(() => emitter.Emit(frame, new(incomplete, rng), Muzzle, End)); Assert.Equal(0, rng.SamplesConsumed);
        Assert.Throws<NotSupportedException>(() => emitter.Emit(frame, new(Assets(), rng), Muzzle, End, movingFrameActive: true));
        Assert.Equal(2, emitter.Emit(frame, new(Assets(), rng), Muzzle, End).Emitted);
    }

    [Fact]
    public void BeamParticlesIncludeTheLastNativeTickAndReadyCompositeIsExplicitlyUnrecovered()
    {
        var weapon = new Rac3ShockCannonSession(); var emitter = new Rac3ShockCannonParticleEmitter(weapon, weapon.Equip(), Source, new(0x1882, 9));
        var particles = new Rac3ParticleSession(Assets(), Rac3ParticleRandom.FromSeed(0)); Rac3ShockCannonFrame frame = null!;
        for (int i = 0; i < 79; i++) frame = weapon.Advance(new(true));
        var first = emitter.Emit(frame, particles, Muzzle, End); Assert.True(first.UnrecoveredReadyCharge); Assert.Equal(6, first.Emitted);
        int beamTicks = 1;
        while (frame.BeamUpdatesRemaining > 0)
        {
            frame = weapon.Advance(new(false)); var emitted = emitter.Emit(frame, particles, Muzzle, End);
            Assert.Equal(6, emitted.Emitted); beamTicks++;
        }
        Assert.Equal(30, beamTicks); Assert.DoesNotContain(frame.Effects, e => e.Kind == Rac3ShockCannonEffectKind.Beam);
    }

    [Fact]
    public void SharedFrameBlobBoundsAndSpriteProjectionAreSourceQualified()
    {
        var defs = Rac3ParticleDefinitions.Parse(DefinitionsBytes(), 65); Assert.Equal(56, defs.Texture(104, 3)); Assert.Equal(0, defs.Texture(0, 0));
        Assert.Throws<InvalidDataException>(() => defs.Texture(104, 112));
        byte[] malformed = DefinitionsBytes(); Put(malformed, 16, int.MinValue);
        Assert.Throws<InvalidDataException>(() => Rac3ParticleDefinitions.Parse(malformed, 65));
        var session = new Rac3ParticleSession(Assets(), Rac3ParticleRandom.FromSeed(0));
        var p = session.Spawn97(Owner, new(Vector4.Zero, Vector3.Zero, 10, 0x123456, 0, 64, 64, 420000, 420000, 0))!;
        var draw = new Rac3ParticleDraw(Enumerable.Repeat(1f, 256).ToArray(), 0x8000000401304000);
        var sprite = draw.Build(p, new(new(0, 0, -1.25f), Vector3.UnitZ, 1f / 420000, int.MaxValue), new(0, 1024, 32), 0x100000)!.Value;
        Assert.Equal(16u, sprite.Color >> 24); Assert.Equal(Owner.Source, sprite.EmitterSource); Assert.Equal(0x8002000555305004ul, sprite.NativeTex0);
        Assert.Equal(64, sprite.CopyNativeDescriptor().Length);
        Assert.Throws<ArgumentException>(() => new Rac3ParticleDraw(new float[255], 0));
        Assert.Throws<InvalidDataException>(() => new Rac3PartTextureLayout(0, 0, 3).Bind(0));
    }

    private static Rac3LevelCore.Core PartCore()
    {
        byte[] index = new byte[0x400], assets = new byte[0x600];
        Put(index, 0x64, 0x100); Put(index, 0x6c, 0x100);
        Put(index, 0xc0, 0); Put(index, 0xc8, 1024); Put(index, 0xcc, 1);
        byte[] definitions = DefinitionsBytes(); Put(definitions, 4, 1); definitions.AsSpan(0x220).Clear(); definitions.CopyTo(index, 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(assets.AsSpan(0x104), 0x80804020); assets[0x500] = 1;
        var header = new Rac3LevelCore.CoreHeader(default, 0, 0, 0, 0, default, default, default,
            default, default, default, default, new(1, 0xc0), default, 0, 0, 0, 0, 0, []);
        return new(null!, null!, header, index, assets, [], []);
    }
    [Fact]
    public void PartBankUsesItsOwnBaseAndTheSharedPs2PaletteCodec()
    {
        var decoded = Rac3ParticleAssets.Read(PartCore(), Source);
        Assert.Equal(new byte[] { 32, 64, 128, 255 }, decoded.Textures[0].Rgba);
        Assert.Equal(Rac3ParticleAssets.AssetKind, decoded.Textures[0].AssetKind);
    }
    [Theory]
    [InlineData(0xc0, -1)]
    [InlineData(0xc8, int.MaxValue)]
    [InlineData(0xcc, 3)]
    [InlineData(0x64, int.MaxValue)]
    [InlineData(0x6c, -1)]
    public void PartBankRejectsMalformedRangesUnsupportedEncodingAndForeignSource(int at, int value)
    {
        var core = PartCore(); Put(core.Index, at, value);
        Assert.Throws<InvalidDataException>(() => Rac3ParticleAssets.Read(core, Source));
        core = PartCore(); Put(core.Index, 0xc4, 1);
        Assert.Throws<NotSupportedException>(() => Rac3ParticleAssets.Read(core, Source));
        Assert.Throws<ArgumentException>(() => Rac3ParticleAssets.Read(PartCore(), Source with { Game = ObpSourceGame.Rac2 }));
    }
}
