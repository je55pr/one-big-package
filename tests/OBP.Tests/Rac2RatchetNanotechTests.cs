using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac2RatchetNanotechTests
{
    [Fact]
    public void OpeningAranosStartsAtFourOfFourNanotech()
    {
        var session = new Rac2RatchetNanotechSession();

        Assert.Equal(4, session.Current);
        Assert.Equal(4, session.Maximum);
        Assert.False(session.IsDepleted);
        Assert.Equal(Rac2RatchetNanotechSession.OrdinaryNativeState, session.NativeState);
        Assert.False(session.PendingHitResolution);
    }

    [Fact]
    public void Msr1RecoveredContactDealsOneNanotech()
    {
        var hostile = new Rac2Class2827HostileSession(Hostile());
        var contact = Assert.Single(
            hostile.ProbeAttackContact(
                Rac2Class2827HostileSession.AttackSequence27,
                Rac2Class2827HostileSession.AttackContactFrameStart).Contacts,
            contact => contact.JointIndex == 0);
        var ratchet = new Rac2RatchetNanotechSession();

        var result = ratchet.Apply(contact.ToPlayerDamageRecord());
        Assert.True(result.Admitted);
        Assert.Equal(1, result.DamageNanotech);
        Assert.Equal(3, result.Current);
        Assert.False(result.Depleted);
        Assert.Equal(Rac2RatchetNanotechSession.ContactHitNativeState, result.NativeState);
        Assert.True(result.PendingHitResolution);

        var resolved = ratchet.ResolveHitReaction();
        Assert.Equal(3, resolved.Current);
        Assert.Equal(Rac2RatchetNanotechSession.OrdinaryNativeState, resolved.NativeState);
        Assert.False(resolved.PendingHitResolution);
    }

    [Fact]
    public void PlayerDamageUsesRecoveredMaskAndCvtWSRounding()
    {
        var ratchet = new Rac2RatchetNanotechSession();

        Assert.False(ratchet.Apply(new Rac2PlayerDamageRecord(0x10000, 1f)).Admitted);
        Assert.False(ratchet.Apply(new Rac2PlayerDamageRecord(
            Rac2RatchetNanotechSession.DamageQueryMask, 0.49f)).Admitted);

        var rounded = ratchet.Apply(new Rac2PlayerDamageRecord(
            Rac2RatchetNanotechSession.DamageQueryMask, 1.6f));

        Assert.True(rounded.Admitted);
        Assert.Equal(2, rounded.DamageNanotech);
        Assert.Equal(2, rounded.Current);
    }

    [Fact]
    public void DamageClampsNanotechAtZero()
    {
        var ratchet = new Rac2RatchetNanotechSession(current: 1, maximum: 4);

        var result = ratchet.Apply(new Rac2PlayerDamageRecord(
            Rac2RatchetNanotechSession.DamageQueryMask, 5f));

        Assert.True(result.Admitted);
        Assert.Equal(0, result.Current);
        Assert.True(result.Depleted);
        Assert.Equal(Rac2RatchetNanotechSession.ContactHitNativeState, result.NativeState);
        Assert.True(result.PendingHitResolution);

        var resolved = ratchet.ResolveHitReaction();
        Assert.Equal(Rac2RatchetNanotechSession.DeathNativeState, resolved.NativeState);
        Assert.False(resolved.PendingHitResolution);
        Assert.False(ratchet.Apply(new Rac2PlayerDamageRecord(
            Rac2RatchetNanotechSession.DamageQueryMask, 1f)).Admitted);
    }

    private static RuntimeDynamicObject Hostile()
    {
        var raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x14, 4), 43);
        var pvar = new byte[0x630];
        BinaryPrimitives.WriteInt32LittleEndian(
            pvar.AsSpan(0x20, 4),
            BitConverter.SingleToInt32Bits(2f));
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
