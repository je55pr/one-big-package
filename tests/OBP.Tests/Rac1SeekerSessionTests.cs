using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1SeekerSessionTests
{
    [Fact]
    public void NativeSeekerEntersChaseWithHysteresisAndAnimationSequenceTransitions()
    {
        var source = Source(42);
        var runtime = new Rac1MobyRuntimeSession();
        var seeker = new Rac1SeekerSession(runtime);
        var first = seeker.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(0, first.NativeState);
        Assert.Equal(3f, first.Health);
        Assert.Equal(1, seeker.Step(42, Vector3.Zero, new(10, 0, 0), false).NativeState);
        Assert.Equal(2, seeker.Step(42, Vector3.Zero, new(10, 0, 0), false).NativeState);
        var moving = seeker.Step(42, Vector3.Zero, new(10, 0, 0), false);
        Assert.Equal(Rac1SeekerSession.SpeedPerTick, moving.Displacement.X, 5);
        Assert.Equal(.08, moving.Displacement.Z, 5);

        // Native 1.4/1.9 thresholds preserve the movement state within the gap.
        var close = seeker.Step(42, Vector3.Zero, new(1, 0, 0), false);
        Assert.Equal(3, close.NativeState);
        Assert.Equal(3, close.Sequence);
        Assert.Equal(0, close.Displacement.X);
        var armed = seeker.Step(42, Vector3.Zero, new(1, 0, 0), true);
        Assert.Equal(4, armed.NativeState);
        Assert.Equal(4, armed.Sequence);
        Assert.Equal(180, armed.Countdown);
        for (int i = 0; i < Rac1SeekerSession.TimedChaseTicks; i++)
            armed = seeker.Step(42, Vector3.Zero, new(10, 0, 0), false);
        Assert.Equal(7, armed.NativeState);
        Assert.True(seeker.Step(42, Vector3.Zero, Vector3.Zero, false).Terminal);
    }

    [Fact]
    public void EnemyDamageUsesNativeHealthAndRejectsRepeatAfterDeath()
    {
        var source = Source(9);
        var runtime = new Rac1MobyRuntimeSession();
        var seeker = new Rac1SeekerSession(runtime);
        seeker.Register(source, RuntimeEntityState.FromAuthored(source));
        var damage = new Rac1GameplayDamageEvent(
            Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(new(Rac1SeekerSession.ClassId, 9)),
            1, 0x10000, owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.True(runtime.CanDispatchDamage(damage));
        for (int i = 0; i < 2; i++)
        {
            var result = runtime.DispatchDamage<Rac1SeekerDamageResult>(damage);
            Assert.False(result.Lethal);
            Assert.Equal(2 - i, result.Frame.Health);
        }
        var lethal = runtime.DispatchDamage<Rac1SeekerDamageResult>(damage);
        Assert.True(lethal.Lethal);
        Assert.True(lethal.Frame.Terminal);
        Assert.False(runtime.CanDispatchDamage(damage));
    }

    [Fact]
    public void NativeBlasterUsesSeekerDamageKindAndNotRawQuarterPoint()
    {
        var source = Source(77);
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1SeekerSession(runtime);
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var hit = Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Blaster,
            new(Rac1SeekerSession.ClassId, 77),
            Rac1WeaponProfiles.BlasterDamage,
            Rac1WeaponProfiles.BlasterFlags);
        Assert.True(runtime.CanDispatchDamage(hit));
        var result = runtime.DispatchDamage<Rac1SeekerDamageResult>(hit);
        Assert.Equal(2.5f, result.Frame.Health);
        Assert.False(result.Lethal);
    }

    [Fact]
    public void InvalidSeekerPayloadsCannotInheritNativeAI()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1SeekerSession(runtime);
        var wrong = Source(1) with { NativeClassId = 252 };
        Assert.Throws<ArgumentException>(() => session.Register(wrong, RuntimeEntityState.FromAuthored(wrong)));
        var noPvar = Source(2) with { NativePayloads = [] };
        Assert.Throws<InvalidDataException>(() => session.Register(noPvar, RuntimeEntityState.FromAuthored(noPvar)));
        session.Register(Source(3), RuntimeEntityState.FromAuthored(Source(3)));
        Assert.Throws<InvalidOperationException>(() =>
            session.Register(Source(3), RuntimeEntityState.FromAuthored(Source(3))));
    }

    [SkippableFact]
    public void AllRetailSeekersShareExactSixHundredTwentyFourByteSourceLayout()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        int total = 0;
        foreach (var level in Rac1DiscIndex.Read(reader).Levels)
        {
            var runtime = new Rac1MobyRuntimeSession();
            var session = new Rac1SeekerSession(runtime);
            var game = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
            if (level.LevelId is 9 or 15)
            {
                var cls = Rac1StaticClasses.Read(Rac1LevelCore.Open(reader, level)).Mobies[Rac1SeekerSession.ClassId];
                foreach (int needed in new[] { 1, 3, 4 })
                {
                    var animation = cls.Sequences.Single(s => s.Index == needed).Value!;
                    Assert.NotEmpty(animation.Frames);
                    Assert.All(animation.Frames, frame =>
                    {
                        float rate = animation.ConstantTransitionRate > 0
                            ? animation.ConstantTransitionRate : frame.TransitionRate;
                        Assert.True(float.IsFinite(rate) && rate > 0 && rate <= 1);
                    });
                }
            }
            foreach (var moby in game.MobyInstances.Where(m => m.OClass == Rac1SeekerSession.ClassId))
            {
                Assert.Equal(Rac1SeekerSession.PVarSize, moby.PVar!.Length);
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(moby.PVar.AsSpan(0x180)));
                Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(moby.PVar.AsSpan(0x164)));
                var source = Source(moby.Index, moby.PVar);
                var entry = session.Register(source, RuntimeEntityState.FromAuthored(source), moby.Rotation.Z);
                Assert.Equal(3f, entry.Health);
                total++;
            }
        }
        Assert.Equal(170, total);
    }

    private static RuntimeDynamicObject Source(int id, byte[]? pvar = null)
    {
        pvar ??= new byte[Rac1SeekerSession.PVarSize];
        return new("rac1", Rac1SeekerSession.ClassId, id, null, "seeker", "seeker",
            new RuntimeObjectTransform(new double[16]), [],
            NativePayloads: [new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
