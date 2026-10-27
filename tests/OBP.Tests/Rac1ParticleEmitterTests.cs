using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1ParticleEmitterTests
{
    // Synthetic boundary tests, not a second source-authored preset.
    internal static byte[] SyntheticParameters()
    {
        byte[] bytes = new byte[Rac1ParticleEmitterParameters.ByteSize];
        foreach (int at in new[] { 0x10, 0x14, 0x18 }) Put(bytes, at, -1000f);
        foreach (int at in new[] { 0x20, 0x24, 0x28 }) Put(bytes, at, 1000f);
        foreach (int at in new[] { 0x48, 0x64, 0x68, 0x6c, 0xbc }) Put(bytes, at, 1f);
        Put(bytes, 0x78, 1000000f); Put(bytes, 0xa8, 210000f); Put(bytes, 0xac, 210000f);
        Put(bytes, 0xb0, .5f); Put(bytes, 0xb4, .25f); Put(bytes, 0xb8, .75f);
        Put(bytes, 0xc0, 4); bytes[0xc4] = 1; bytes[0xc6] = 0xf1; bytes[0xc7] = 4;
        return bytes;
    }

    internal static void Put(byte[] bytes, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value);
    internal static void Put(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
    internal static Rac1ParticleEmitter Owner(byte[] bytes, Vector3? position = null, Vector3? rotation = null) => new(
        new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "LEVEL1", "moby", "285"), new(27, 285),
        Rac1ParticleEmitterParameters.Parse(bytes), position ?? new(100), rotation ?? Vector3.Zero);
    internal static Rac1ParticleEmitterView Seeing(Vector3 position) => new(position - Vector3.UnitY * 30,
        Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ, .63f, .48825f);
    internal static Rac1EffectAssets Assets() => new(new Dictionary<int, int[]> { [4] = [42] },
        new Dictionary<int, RuntimeTexture>(), new Dictionary<int, RuntimeTexture>());

    [Fact]
    public void ConstructorPreservesNativeTextureBlendDepthAndDrawCount()
    {
        int draws = 0;
        var session = new Rac1ParticleSession(Assets(), new(() => { draws++; return 0; }));
        var owner = Owner(SyntheticParameters());
        Assert.Equal(new(Rac1ParticleEmissionStatus.Emitted, 1, 1), session.Emit(owner, Seeing(owner.Position)));
        var particle = Assert.Single(session.Live);
        Assert.Equal(8, draws); Assert.Equal(6, particle.Type); Assert.Equal(42, particle.Texture);
        Assert.Equal(Rac1ParticleSession.Blend.Additive, particle.Blending); Assert.Equal(0x74, particle.DepthRange);
        Assert.Equal(0xffbf3f7fu, particle.Color); Assert.Equal(1f, particle.Size);
        var snapshot = Assert.Single(session.CaptureSprites());
        Assert.Equal(owner.Source, snapshot.Source); Assert.Equal(owner.Owner, snapshot.Owner);
        session.Step(Vector3.Zero);
        Assert.Equal(3, particle.Remaining); Assert.Equal(4, snapshot.Remaining);
        Assert.Equal(3, Assert.Single(session.CaptureSprites()).Remaining);
        Assert.True(Rac1ParticleSession.SupportsType(6)); Assert.False(Rac1ParticleSession.SupportsType(85));
    }

    [Fact]
    public void CullingFreezesCountdownAndNoEmissionPathsDrawNoRandomNumbers()
    {
        byte[] bytes = SyntheticParameters(); Put(bytes, 0xc8, 2); bytes[0xc5] = 3;
        int draws = 0;
        var session = new Rac1ParticleSession(Assets(), new(() => { draws++; return 0; }));
        var owner = Owner(bytes); var view = Seeing(owner.Position);
        Assert.Equal(Rac1ParticleEmissionStatus.Culled, session.Emit(owner, null).Status);
        Assert.Equal(2, owner.BurstCountdown);
        Assert.Equal(Rac1ParticleEmissionStatus.Culled, session.Emit(owner, Seeing(owner.Position + new Vector3(1000))).Status);
        for (int i = 0; i < 2; i++) Assert.Equal(Rac1ParticleEmissionStatus.Countdown, session.Emit(owner, view).Status);
        Assert.Equal(0, draws); session.Emit(owner, view); Assert.Equal(8, draws);
        Assert.Equal(3, owner.BurstCountdown);
        for (int i = 0; i < 3; i++) Assert.Equal(Rac1ParticleEmissionStatus.Countdown, session.Emit(owner, view).Status);
        session.Emit(owner, view); Assert.Equal(16, draws);
        owner.StopEmission(); Assert.Equal(Rac1ParticleEmissionStatus.Stopped, session.Emit(owner, view).Status);
        owner.Retire(); Assert.Equal(Rac1ParticleEmissionStatus.Retired, session.Emit(owner, view).Status);
        Assert.Equal(16, draws);
    }

    [Fact]
    public void FullPoolStillConsumesTheWholeBurstAndReusedSlotsHaveNewIdentities()
    {
        byte[] bytes = SyntheticParameters(); bytes[0xc4] = 3;
        int draws = 0;
        var session = new Rac1ParticleSession(Assets(), new(() => { draws++; return 0; }), 1);
        var owner = Owner(bytes); var view = Seeing(owner.Position);
        Assert.Equal(new(Rac1ParticleEmissionStatus.Emitted, 3, 1), session.Emit(owner, view));
        var first = Assert.Single(session.Live);
        Assert.Equal(new(Rac1ParticleEmissionStatus.PoolFull, 3, 0), session.Emit(owner, view));
        Assert.Equal(48, draws); Assert.True(session.IsLive(first));
        session.Clear(); session.Emit(owner, view);
        var replacement = Assert.Single(session.Live);
        Assert.Equal(first.Slot, replacement.Slot); Assert.NotEqual(first.Identity, replacement.Identity);
        session.Remove(first); Assert.True(session.IsLive(replacement)); Assert.False(session.IsLive(first));
        owner.StopEmission(); for (int tick = 0; tick < 4; tick++) session.Step(Vector3.Zero);
        Assert.Empty(session.CaptureSprites());
    }

    [Fact]
    public void Native2048RecordCapacityBoundsSustainedBurstsAndRetirementClearsThem()
    {
        byte[] bytes = SyntheticParameters(); bytes[0xc4] = 255;
        int draws = 0;
        var session = new Rac1ParticleSession(Assets(), new(() => { draws++; return 0; }));
        var owner = Owner(bytes); var view = Seeing(owner.Position);
        for (int burst = 0; burst < 10; burst++) session.Emit(owner, view);
        Assert.Equal(2048, session.Count); Assert.Equal(20400, draws);
        Assert.Equal(Enumerable.Range(0, 2048), session.Live.Select(p => p.Slot));
        owner.Retire(); session.Step(Vector3.Zero); Assert.Empty(session.Live);
    }

    [Fact]
    public void OwnerWritesAffectNewAttachmentsAndAlreadyLiveParticleParameters()
    {
        byte[] bytes = SyntheticParameters(); Put(bytes, 0x80, 1f); Put(bytes, 0x90, 1f);
        var owner = Owner(bytes); var session = new Rac1ParticleSession(Assets(), new(() => 0));
        session.Emit(owner, Seeing(owner.Position)); var first = Assert.Single(session.Live);
        Put(bytes, 0x70, 8); Put(bytes, 0, .5f); Put(bytes, 0x40, 10f); Put(bytes, 0x30, 500f);
        owner.ApplyNativeFrame(Rac1ParticleEmitterParameters.Parse(bytes), new(200), Vector3.Zero);
        session.Emit(owner, Seeing(owner.Position));
        var second = session.Live.Last(); Assert.Equal(new Vector3(200), second.Position);
        Assert.Equal(new Vector3(100), first.Position);
        session.Step(Vector3.Zero); Assert.Equal(101, first.Position.X); Assert.Equal(1.5f, first.Velocity.X);
        Assert.InRange(first.Size, 1.00238f, 1.00239f);
        owner.StopEmission(); session.Step(Vector3.Zero); Assert.Equal(2, session.Count);
        owner.Retire(); session.Step(Vector3.Zero); Assert.Empty(session.Live);
        Assert.Throws<InvalidOperationException>(() => owner.ApplyNativeFrame(Rac1ParticleEmitterParameters.Parse(bytes), Vector3.Zero, Vector3.Zero));
    }

    [Fact]
    public void OffsetYUsesTheRetailYToZRangeAndAlphaAndAnglePackBeforeTheirStep()
    {
        byte[] bytes = SyntheticParameters(); Put(bytes, 0xcc, 1f); Put(bytes, 0xd0, 2f); Put(bytes, 0xd4, 4f);
        Put(bytes, 0x70, 4); Put(bytes, 0x38, -.25f); Put(bytes, 0x34, .25f);
        var owner = Owner(bytes); var session = new Rac1ParticleSession(Assets(), new(() => 16384));
        session.Emit(owner, Seeing(owner.Position)); var p = Assert.Single(session.Live);
        Assert.Equal(new Vector3(100, 101, 100), p.Position);
        session.Step(Vector3.Zero); Assert.Equal(255u, p.Color >> 24); Assert.Equal(0, p.Angle);
        session.Step(Vector3.Zero); Assert.Equal(191u, p.Color >> 24); Assert.Equal(63, p.Angle);
        session.Step(Vector3.Zero); Assert.Equal(127u, p.Color >> 24);
        session.Step(Vector3.Zero); Assert.Empty(session.Live);
    }

    [Fact]
    public void AlphaAndColorOscillateOnlyAtFlaggedLimitsAndBoundsCanKill()
    {
        byte[] bytes = SyntheticParameters(); Put(bytes, 0xc0, 20); Put(bytes, 0x70, 0x996);
        Put(bytes, 0x4c, .75f); Put(bytes, 0x38, .75f); Put(bytes, 0x44, .25f);
        var owner = Owner(bytes); var session = new Rac1ParticleSession(Assets(), new(() => 0));
        session.Emit(owner, Seeing(owner.Position)); var p = Assert.Single(session.Live);
        session.Step(Vector3.Zero); Assert.Equal(255u, p.Color & 255); // red crosses max, flips down
        session.Step(Vector3.Zero); Assert.InRange(p.Color & 255, 62u, 64u);
        session.Step(Vector3.Zero); Assert.Equal(0u, p.Color & 255); // crosses min, flips up
        session.Step(Vector3.Zero); Assert.InRange(p.Color & 255, 190u, 192u);
        Put(bytes, 0x20, 99f); owner.ApplyNativeFrame(Rac1ParticleEmitterParameters.Parse(bytes), owner.Position, owner.Rotation);
        session.Step(Vector3.Zero); Assert.Empty(session.Live);
    }

    [Fact]
    public void MissingTexturesMalformedParametersAndUnrecoveredCollisionFailExplicitly()
    {
        byte[] bytes = SyntheticParameters();
        Assert.Throws<InvalidDataException>(() => Rac1ParticleEmitterParameters.Parse(bytes.AsSpan(0, 0xdf)));
        Put(bytes, 0, float.NaN); Assert.Throws<InvalidDataException>(() => Rac1ParticleEmitterParameters.Parse(bytes));
        bytes = SyntheticParameters(); Put(bytes, 0x70, 1); Assert.Throws<NotSupportedException>(() => Owner(bytes));
        bytes = SyntheticParameters(); bytes[0xc7] = 6;
        var owner = Owner(bytes); var session = new Rac1ParticleSession(Assets(), new(() => throw new Exception("RNG must not run")));
        Assert.Throws<InvalidDataException>(() => session.Emit(owner, Seeing(owner.Position)));
        Assert.Empty(session.Live); Assert.Equal(0, owner.BurstCountdown);
    }
}
