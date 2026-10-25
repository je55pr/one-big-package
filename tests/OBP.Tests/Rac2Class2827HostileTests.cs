using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac2Class2827HostileTests
{
    [Fact]
    public void ReadsAuthoredAranosHealthCooldownAndBolts()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);

        var authored = Rac2Class2827HostileSession.ReadAuthored(source);

        Assert.NotNull(authored);
        Assert.Equal(2f, authored!.Health);
        Assert.Equal((short)0, authored.HitCooldownTicks);
        Assert.Equal(1f, authored.AttackDamageHp);
        Assert.Equal(Rac2Class2827HostileSession.OpeningRoomAuthoredMode, authored.AuthoredMode);
        Assert.Equal(43, authored.AuthoredBolts);

        var session = new Rac2Class2827HostileSession(source);
        Assert.NotNull(session.NativeState);
        Assert.Equal(
            new Rac2NativeStateSnapshot(
                Rac2Class2827HostileSession.OpeningRoomInitialNativeState,
                PreviousState: 0,
                Rac2Class2827HostileSession.OpeningRoomInitialTransitionMode,
                StateTicks: 0),
            session.NativeState!.Snapshot());
    }

    [Fact]
    public void State20DamageKillsTwoHpHostileAndStartsRetailCooldown()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);
        var session = new Rac2Class2827HostileSession(source);

        var result = session.Apply(Rac2DamageRuntime.FromPlayerState20(source));

        Assert.True(result.Admitted);
        Assert.True(result.Terminal);
        Assert.Equal(0f, result.Health);
        Assert.Equal(Rac2Class2827HostileSession.HitCooldownTicks, result.HitCooldownTicks);
        Assert.Equal(
            OBP.Runtime.Gameplay.RuntimeEntityPresence.Inactive,
            result.EntityState.Presentation.Presence);
        Assert.Equal(
            new Rac2NativeStateSnapshot(
                Rac2Class2827HostileSession.DeathNativeState,
                Rac2Class2827HostileSession.OpeningRoomInitialNativeState,
                Rac2Class2827HostileSession.DeathTransitionMode,
                StateTicks: 0),
            session.NativeState!.Snapshot());
    }

    [Fact]
    public void ActiveHitCooldownRejectsDamageUntilItExpires()
    {
        var source = Hostile(health: 4f, cooldown: 0, bolts: 43);
        var session = new Rac2Class2827HostileSession(source);

        var first = session.Apply(Rac2DamageRuntime.FromPlayerState20(source));
        var blocked = session.Apply(Rac2DamageRuntime.FromPlayerState20(source));
        session.TickCooldown(Rac2Class2827HostileSession.HitCooldownTicks);
        var second = session.Apply(Rac2DamageRuntime.FromPlayerState20(source));

        Assert.True(first.Admitted);
        Assert.False(blocked.Admitted);
        Assert.True(second.Admitted);
        Assert.Equal(0f, second.Health);
        Assert.True(second.Terminal);
    }

    [Fact]
    public void State12AttackAdmissionUsesRecoveredDistanceAndFacingBounds()
    {
        Assert.True(Rac2Class2827HostileSession.ShouldEnterAttack(2.79, 0.20));
        Assert.False(Rac2Class2827HostileSession.ShouldEnterAttack(2.8, 0.20));
        Assert.False(Rac2Class2827HostileSession.ShouldEnterAttack(2.79,
            Rac2Class2827HostileSession.AttackFacingErrorExclusive));
    }

    [Fact]
    public void State12AttackAdmissionUsesRecoveredNativeTransitionBookkeeping()
    {
        var session = new Rac2Class2827HostileSession(Hostile(2f, 0, 43));
        session.NativeState!.Transition(Rac2Class2827HostileSession.ChaseNativeState);
        session.AdvanceNativeStateTicks(7);

        Assert.False(session.TryEnterAttack(2.8, 0.20));
        Assert.Equal(Rac2Class2827HostileSession.ChaseNativeState, session.NativeState.CurrentState);
        Assert.True(session.TryEnterAttack(2.79, 0.20));
        Assert.Equal(
            new Rac2NativeStateSnapshot(
                Rac2Class2827HostileSession.AttackNativeState,
                Rac2Class2827HostileSession.ChaseNativeState,
                Rac2Class2827HostileSession.OpeningRoomInitialTransitionMode,
                StateTicks: 0),
            session.NativeState.Snapshot());
    }

    [Fact]
    public void State13ContactWindowPublishesFourRecoveredVolumes()
    {
        var session = new Rac2Class2827HostileSession(Hostile(2f, 0, 43));

        var before = session.ProbeAttackContact(
            Rac2Class2827HostileSession.AttackSequence27, 18.99f);
        var active = session.ProbeAttackContact(
            Rac2Class2827HostileSession.AttackSequence27, 19f);
        var end = session.ProbeAttackContact(
            Rac2Class2827HostileSession.AttackSequence16, 25f);

        Assert.Empty(before.Contacts);
        Assert.Equal([0, 1, 2, 9], active.Contacts.Select(c => c.JointIndex));
        Assert.Equal([0.35f, 0.15f, 0.15f, 0.35f], active.Contacts.Select(c => c.Radius));
        Assert.All(active.Contacts, contact =>
        {
            Assert.Equal(1f, contact.DamageHp);
            Assert.Equal(1f, contact.UnitScale);
        });
        Assert.Equal(4, end.Contacts.Count);
        Assert.Empty(session.ProbeAttackContact(8, 20f).Contacts);
    }

    [Fact]
    public void DamageWithoutRetailQueryMaskIsIgnored()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);
        var session = new Rac2Class2827HostileSession(source);
        var damage = new Rac2GameplayDamageEvent(
            Rac2GameplayEntityRef.Player,
            Rac2GameplayEntityRef.Moby(source),
            new Rac2CollisionDamage(0, 0, 1, 71, 2f, 1));

        var result = session.Apply(damage);

        Assert.False(result.Admitted);
        Assert.Equal(2f, result.Health);
        Assert.False(result.Terminal);
    }

    private static RuntimeDynamicObject Hostile(float health, short cooldown, int bolts)
    {
        var raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x14, 4), bolts);
        var pvar = new byte[0x630];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(0x20, 4),
            BitConverter.SingleToInt32Bits(health));
        BinaryPrimitives.WriteInt16LittleEndian(pvar.AsSpan(0x26, 2), cooldown);
        pvar[0x34] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x27C, 4), 1);

        return new RuntimeDynamicObject(
            ObpSourceGame.Rac2,
            Rac2Class2827HostileSession.NativeClassId,
            205,
            11,
            "moby:2827",
            "level:0:moby:205",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [
                new RuntimeOpaquePayload("rac2-moby-instance-0x88", raw),
                new RuntimeOpaquePayload("rac2-pvar", pvar),
            ]);
    }
}
