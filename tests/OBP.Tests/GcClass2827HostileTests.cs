using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class GcClass2827HostileTests
{
    [Fact]
    public void ReadsAuthoredAranosHealthCooldownAndBolts()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);

        var authored = GcClass2827HostileSession.ReadAuthored(source);

        Assert.NotNull(authored);
        Assert.Equal(2f, authored!.Health);
        Assert.Equal((short)0, authored.HitCooldownTicks);
        Assert.Equal(43, authored.AuthoredBolts);
    }

    [Fact]
    public void State20DamageKillsTwoHpHostileAndStartsRetailCooldown()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);
        var session = new GcClass2827HostileSession(source);

        var result = session.Apply(GcDamageRuntime.FromPlayerState20(source));

        Assert.True(result.Admitted);
        Assert.True(result.Terminal);
        Assert.Equal(0f, result.Health);
        Assert.Equal(GcClass2827HostileSession.HitCooldownTicks, result.HitCooldownTicks);
        Assert.Equal(
            OBP.Runtime.Gameplay.RuntimeEntityPresence.Inactive,
            result.EntityState.Presentation.Presence);
    }

    [Fact]
    public void ActiveHitCooldownRejectsDamageUntilItExpires()
    {
        var source = Hostile(health: 4f, cooldown: 0, bolts: 43);
        var session = new GcClass2827HostileSession(source);

        var first = session.Apply(GcDamageRuntime.FromPlayerState20(source));
        var blocked = session.Apply(GcDamageRuntime.FromPlayerState20(source));
        session.TickCooldown(GcClass2827HostileSession.HitCooldownTicks);
        var second = session.Apply(GcDamageRuntime.FromPlayerState20(source));

        Assert.True(first.Admitted);
        Assert.False(blocked.Admitted);
        Assert.True(second.Admitted);
        Assert.Equal(0f, second.Health);
        Assert.True(second.Terminal);
    }

    [Fact]
    public void DamageWithoutRetailQueryMaskIsIgnored()
    {
        var source = Hostile(health: 2f, cooldown: 0, bolts: 43);
        var session = new GcClass2827HostileSession(source);
        var damage = new GcGameplayDamageEvent(
            GcGameplayEntityRef.Player,
            GcGameplayEntityRef.Moby(source),
            new GcCollisionDamage(0, 0, 1, 71, 2f, 1));

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
