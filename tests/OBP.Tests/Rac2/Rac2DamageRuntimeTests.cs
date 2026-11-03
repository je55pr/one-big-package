using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac2DamageRuntimeTests
{
    [Fact]
    public void State20EventPreservesRecoveredTupleAndAuthoredTarget()
    {
        var target = Class500(instanceIndex: 17, uid: 91, pvarC8: 0);

        var damage = Rac2DamageRuntime.FromPlayerState20(target);

        Assert.Equal(Rac2GameplayEntityRef.Player, damage.Source);
        Assert.Equal(Rac2GameplayEntityKind.Moby, damage.Target.Kind);
        Assert.Equal(500, damage.Target.NativeClassId);
        Assert.Equal(17, damage.Target.RuntimeId);
        Assert.Equal(Rac2PlayerAttackDamage.State20, damage.Damage);
        Assert.True(damage.Target.Matches(target));
    }

    [Fact]
    public void TransportSequencesEventsWithoutApplyingConsequences()
    {
        var target = Class500(instanceIndex: 17, uid: 91, pvarC8: 0);
        var initial = RuntimeEntityState.FromAuthored(target);
        var session = new Rac2DamageTransportSession();
        var observed = new List<Rac2GameplayDamageDispatch>();
        session.Published += observed.Add;

        var first = session.Publish(Rac2DamageRuntime.FromPlayerState20(target));
        var second = session.Publish(Rac2DamageRuntime.FromPlayerState20(target));

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(2, session.Sequence);
        Assert.Equal([first, second], observed);
        Assert.Equal(RuntimeEntityPresence.Active, initial.Presentation.Presence);
    }

    [Fact]
    public void MatchingState20DamageAppliesRecoveredClass500Lifetime()
    {
        var target = Class500(instanceIndex: 17, uid: 91, pvarC8: 0);
        var initial = RuntimeEntityState.FromAuthored(target);
        var damage = Rac2DamageRuntime.FromPlayerState20(target);

        var result = Rac2DamageRuntime.ApplyClass500Consequence(
            target,
            initial,
            damage);

        Assert.NotNull(result);
        Assert.Equal(Rac2Class500PostBreakRoute.Deactivate, result!.Route);
        Assert.Equal(
            RuntimeEntityPresence.Inactive,
            result.EntityState.Presentation.Presence);
    }

    [Fact]
    public void NonBreakingDamageDoesNotInventAClass500Transition()
    {
        var target = Class500(instanceIndex: 17, uid: 91, pvarC8: 0);
        var initial = RuntimeEntityState.FromAuthored(target);
        var damage = new Rac2GameplayDamageEvent(
            Rac2GameplayEntityRef.Player,
            Rac2GameplayEntityRef.Moby(target),
            new Rac2CollisionDamage(0, 0, 0, 0, 2f, 0));

        var result = Rac2DamageRuntime.ApplyClass500Consequence(
            target,
            initial,
            damage);

        Assert.Null(result);
        Assert.Equal(RuntimeEntityPresence.Active, initial.Presentation.Presence);
    }

    [Fact]
    public void ConsequenceRejectsDamageAddressedToAnotherAuthoredMoby()
    {
        var target = Class500(instanceIndex: 17, uid: 91, pvarC8: 0);
        var other = Class500(instanceIndex: 18, uid: 92, pvarC8: 0);
        var damage = Rac2DamageRuntime.FromPlayerState20(other);

        Assert.Throws<InvalidOperationException>(() =>
            Rac2DamageRuntime.ApplyClass500Consequence(
                target,
                RuntimeEntityState.FromAuthored(target),
                damage));
    }

    private static RuntimeDynamicObject Class500(
        int instanceIndex,
        int uid,
        byte pvarC8)
    {
        var raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), uid);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x14, 4), 13);
        var pvar = new byte[0x110];
        pvar[0xC8] = pvarC8;

        return new RuntimeDynamicObject(
            ObpSourceGame.Rac2,
            500,
            instanceIndex,
            uid,
            "moby:500",
            $"level:0:moby:{instanceIndex}",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload("rac2-moby-instance-0x88", raw),
                new RuntimeOpaquePayload("rac2-pvar", pvar),
            ]);
    }
}
