using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1NovalisBirdbotTests
{
    private static RuntimeDynamicObject Birdbot(int instance = 309, float hp = 1)
    {
        var pvar = new byte[Rac1NovalisBirdbotSession.PVarSize];
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x20), hp);
        double[] matrix = new double[16];
        matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1;
        return new("rac1", Rac1NovalisBirdbotSession.ClassId, instance, null, "moby:459",
            $"birdbot:{instance}", new(matrix), [],
            [new(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }

    [Fact]
    public void HostPursuesFiresOnCooldownAndDropsTargetAfterSeparation()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1NovalisBirdbotSession(runtime);
        var source = Birdbot();
        var idle = session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(Rac1NovalisBirdbotSession.HostMode.Idle, idle.Mode);
        Assert.Equal(1, idle.Health);
        Assert.Equal(1, idle.Sequence);
        Assert.False(session.Step(309, new(Vector3.Zero, new(100, 0, 0), true)).Fire);
        var approaching = session.Step(309, new(Vector3.Zero, new(9, 0, 0), true));
        Assert.Equal(Rac1NovalisBirdbotSession.HostMode.Pursuing, approaching.Mode);
        var firing = session.Step(309, new(Vector3.Zero, new(9, 0, 0), true));
        Assert.True(firing.Fire);
        Assert.Equal(Rac1NovalisBirdbotSession.HostMode.Shooting, firing.Mode);
        Assert.True(firing.Cooldown > 0);
        Assert.False(session.Step(309, new(Vector3.Zero, new(9, 0, 0), true)).Fire);
        var recovered = session.Step(309, new(Vector3.Zero, new(9, 0, 0), true, AnimationWrapped: true));
        Assert.Equal(Rac1NovalisBirdbotSession.HostMode.Pursuing, recovered.Mode);
        Assert.False(session.Step(309, new(Vector3.Zero, new(9, 0, 0), true)).Fire);
        Assert.Equal(Rac1NovalisBirdbotSession.HostMode.Idle,
            session.Step(309, new(Vector3.Zero, new(100, 0, 0), true)).Mode);
    }

    [Fact]
    public void DamagedBirdbotTerminatesAfterRetailClipAndRejectsFurtherDamage()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1NovalisBirdbotSession(runtime);
        var source = Birdbot();
        session.Register(source, RuntimeEntityState.FromAuthored(source));
        var target = Rac1GameplayEntityRef.Moby(new(459, 309));
        var wrench = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.PlayerWrench, target, 1,
            Rac1WrenchAttackProfile.NativeDamageFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.True(runtime.CanDispatchDamage(wrench));
        var result = runtime.DispatchDamage<(Rac1NovalisBirdbotSession.Probe Probe, bool Lethal)>(wrench);
        Assert.True(result.Lethal);
        Assert.Equal(Rac1NovalisBirdbotSession.DeathSequence, result.Probe.Sequence);
        Assert.False(runtime.CanDispatchDamage(wrench));
        Assert.False(session.Step(309, new(Vector3.Zero, Vector3.One, true)).Terminal);
        Assert.True(session.Step(309, new(Vector3.Zero, Vector3.One, true, AnimationWrapped: true)).Terminal);
        Assert.False(runtime.Require(source).IsActive);
    }

    [Fact]
    public void SourceContractRejectsUnrelatedClassIncorrectPvarAndInvalidHealth()
    {
        var session = new Rac1NovalisBirdbotSession(new());
        var source = Birdbot();
        var invalid = source with { NativeClassId = 577 };
        Assert.Throws<ArgumentException>(() => session.Register(invalid, RuntimeEntityState.FromAuthored(invalid)));
        invalid = source with { NativePayloads = [] };
        Assert.Throws<InvalidDataException>(() => session.Register(invalid, RuntimeEntityState.FromAuthored(invalid)));
        invalid = Birdbot(hp: -1);
        Assert.Throws<InvalidDataException>(() => session.Register(invalid, RuntimeEntityState.FromAuthored(invalid)));
    }

    [SkippableFact]
    public void RetailNovalisBirdbotsHaveAuthoritativePlacementsAndSequenceCarrier()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac1WorldImport.Build(reader, 1);
        var birds = world.DynamicObjects!.Where(x => x.NativeClassId == 459).ToArray();
        Assert.Equal(25, birds.Length);
        foreach (var bird in birds)
        {
            var pvar = Assert.Single(bird.NativePayloads!, x => x.Format == Rac1BoltCrate.PVarPayloadFormat).Data;
            Assert.Equal(0x240, pvar.Length);
            Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(pvar.AsSpan(0x20)));
            Assert.Equal(39, Rac1EnemyMotion.RewardCentre(bird));
        }
        var level = OBP.RAC1.Level.Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 1);
        var core = OBP.RAC1.Level.Rac1LevelCore.Open(reader, level);
        var clips = OBP.RAC1.Level.Rac1StaticClasses.Read(core).Mobies[459].Sequences;
        Assert.Equal(21, clips.Count(x => x.Value is not null));
        Assert.Equal(63, clips[Rac1NovalisBirdbotSession.DeathSequence].Value!.Frames.Count);
    }
}
