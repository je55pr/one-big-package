using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1NovalisPeckbotTests
{
    private static RuntimeDynamicObject Peckbot(int instance, bool startsStanding = true)
    {
        var pvar = new byte[Rac1NovalisPeckbotSession.PVarSize];
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x20a), startsStanding ? (short)1 : (short)0);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x21c), -1);
        var matrix = new double[16]; matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1;
        return new("rac1", 577, instance, null, "moby:577", $"peckbot:{instance}",
            new(matrix), [], [new(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }

    private static Rac1PickupRandom AlwaysAlert => new(() => 0);

    [Fact]
    public void StandingPeckbotAcquiresPursuesAttacksAndReturnsHome()
    {
        var session = new Rac1NovalisPeckbotSession(new(), AlwaysAlert);
        var moby = Peckbot(586);
        var probe = session.Register(moby, RuntimeEntityState.FromAuthored(moby));
        Assert.Equal(1, probe.NativeState);
        Assert.Equal(4, probe.Sequence);
        Assert.Equal(1, probe.Health);

        var target = new Vector3(8, 0, 0);
        probe = session.Step(586, new(Vector3.Zero, target));
        Assert.Equal(2, probe.NativeState);
        Assert.Equal(5, probe.Sequence);
        probe = session.Step(586, new(Vector3.Zero, target));
        Assert.Equal(3, probe.NativeState);
        Assert.Equal(2, probe.Sequence);
        probe = session.Step(586, new(Vector3.Zero, target));
        Assert.Equal(target, probe.Destination);

        probe = session.Step(586, new(new(7.2f, 0, 0), target));
        Assert.Equal(4, probe.NativeState);
        Assert.Equal(3, probe.Sequence);
        probe = session.Step(586, new(new(7.2f, 0, 0), target, AnimationPosition: 13f));
        Assert.True(probe.PeckContact);
        Assert.False(session.Step(586, new(new(7.2f, 0, 0), target, AnimationPosition: 14)).PeckContact);
        probe = session.Step(586, new(new(7.2f, 0, 0), target, AnimationPosition: 13, AnimationWrapped: true));
        Assert.True(probe.PeckContact);
        probe = session.Step(586, new(new(7.2f, 0, 0), new(80, 0, 0), AnimationWrapped: true));
        Assert.Equal(3, probe.NativeState);
        probe = session.Step(586, new(new(7.2f, 0, 0), new(80, 0, 0)));
        Assert.Equal(5, probe.NativeState);
        Assert.Equal(Vector3.Zero, session.Step(586, new(new(7.2f, 0, 0), new(80, 0, 0))).Destination);
        Assert.Equal(1, session.Step(586, new(Vector3.Zero, new(80, 0, 0))).NativeState);
    }

    [Fact]
    public void DamageAcceptsWrenchAndBombOnlyThenTerminatesAfterDeath()
    {
        var runtime = new Rac1MobyRuntimeSession();
        var session = new Rac1NovalisPeckbotSession(runtime);
        var source = Peckbot(579, startsStanding: false);
        var probe = session.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(0xe, probe.NativeState);
        Assert.True(probe.InitialLoiter);
        var target = Rac1GameplayEntityRef.Moby(new(577, 579));
        var wrench = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.PlayerWrench, target, 1,
            Rac1WrenchAttackProfile.NativeDamageFlags,
            owner: Rac1GameplayDamageOwner.FromEntity(Rac1GameplayEntityRef.Player));
        Assert.True(runtime.CanDispatchDamage(wrench));
        var hit = runtime.DispatchDamage<Rac1NovalisPeckbotSession.DamageResult>(wrench);
        Assert.True(hit.Lethal);
        Assert.Equal(0x63, hit.Probe.NativeState);
        Assert.Equal(7, hit.Probe.Sequence);
        Assert.False(runtime.CanDispatchDamage(wrench));
        Assert.False(session.Step(579, new(Vector3.Zero, Vector3.One)).Terminal);
        Assert.True(session.Step(579, new(Vector3.Zero, Vector3.One, AnimationWrapped: true)).Terminal);
        Assert.False(probe.Instance.IsActive);
    }

    [Fact]
    public void OnlyOriginalPvarAndNovalisClassAdmitted()
    {
        var session = new Rac1NovalisPeckbotSession(new());
        var fake = Peckbot(580);
        Assert.Throws<ArgumentException>(() => session.Register(fake with { NativeClassId = 459 }, RuntimeEntityState.FromAuthored(fake with { NativeClassId = 459 })));
        Assert.Throws<InvalidDataException>(() => session.Register(fake with { NativePayloads = [] }, RuntimeEntityState.FromAuthored(fake with { NativePayloads = [] })));
    }
}
