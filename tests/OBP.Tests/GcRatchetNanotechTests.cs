using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class GcRatchetNanotechTests
{
    [Fact]
    public void OpeningAranosStartsAtFourOfFourNanotech()
    {
        var session = new GcRatchetNanotechSession();

        Assert.Equal(4, session.Current);
        Assert.Equal(4, session.Maximum);
        Assert.False(session.IsDepleted);
        Assert.Equal(GcRatchetNanotechSession.OrdinaryNativeState, session.NativeState);
        Assert.False(session.PendingHitResolution);
    }

    [Fact]
    public void Msr1RecoveredContactDealsOneNanotech()
    {
        var hostile = new GcClass2827HostileSession(Hostile());
        var contact = Assert.Single(
            hostile.ProbeAttackContact(
                GcClass2827HostileSession.AttackSequence27,
                GcClass2827HostileSession.AttackContactFrameStart).Contacts,
            contact => contact.JointIndex == 0);
        var ratchet = new GcRatchetNanotechSession();

        var result = ratchet.Apply(contact.ToPlayerDamageRecord());
        Assert.True(result.Admitted);
        Assert.Equal(1, result.DamageNanotech);
        Assert.Equal(3, result.Current);
        Assert.False(result.Depleted);
        Assert.Equal(GcRatchetNanotechSession.ContactHitNativeState, result.NativeState);
        Assert.True(result.PendingHitResolution);

        var resolved = ratchet.ResolveHitReaction();
        Assert.Equal(3, resolved.Current);
        Assert.Equal(GcRatchetNanotechSession.OrdinaryNativeState, resolved.NativeState);
        Assert.False(resolved.PendingHitResolution);
    }

    [Fact]
    public void PlayerDamageUsesRecoveredMaskAndCvtWSRounding()
    {
        var ratchet = new GcRatchetNanotechSession();

        Assert.False(ratchet.Apply(new GcPlayerDamageRecord(0x10000, 1f)).Admitted);
        Assert.False(ratchet.Apply(new GcPlayerDamageRecord(
            GcRatchetNanotechSession.DamageQueryMask, 0.49f)).Admitted);

        var rounded = ratchet.Apply(new GcPlayerDamageRecord(
            GcRatchetNanotechSession.DamageQueryMask, 1.6f));

        Assert.True(rounded.Admitted);
        Assert.Equal(2, rounded.DamageNanotech);
        Assert.Equal(2, rounded.Current);
    }

    [Fact]
    public void DamageClampsNanotechAtZero()
    {
        var ratchet = new GcRatchetNanotechSession(current: 1, maximum: 4);

        var result = ratchet.Apply(new GcPlayerDamageRecord(
            GcRatchetNanotechSession.DamageQueryMask, 5f));

        Assert.True(result.Admitted);
        Assert.Equal(0, result.Current);
        Assert.True(result.Depleted);
        Assert.Equal(GcRatchetNanotechSession.ContactHitNativeState, result.NativeState);
        Assert.True(result.PendingHitResolution);

        var resolved = ratchet.ResolveHitReaction();
        Assert.Equal(GcRatchetNanotechSession.DeathNativeState, resolved.NativeState);
        Assert.False(resolved.PendingHitResolution);
        Assert.False(ratchet.Apply(new GcPlayerDamageRecord(
            GcRatchetNanotechSession.DamageQueryMask, 1f)).Admitted);
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
            "rac2",
            GcClass2827HostileSession.NativeClassId,
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
