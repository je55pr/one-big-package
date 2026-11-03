using System.Buffers.Binary;
using OBP.Core;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WorldContextTests
{
    [Fact]
    public void CrateDamageAndRewardsUseTheSameWorldRuntimeAndPickupIdentitySpace()
    {
        var context = new Rac1WorldContext(0, Rac1PickupRandom.FromSeed(1440));
        var source = Crate();
        var instance = context.BoltCrates.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Same(instance, context.MobyRuntime.Require(source));
        var damage = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(instance.Key), 2, 0x10000);
        var admission = context.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage);
        var broken = context.BoltCrates.CompleteDamage(admission, 19);
        Assert.False(context.MobyRuntime.Require(source).IsActive);
        Assert.Equal(1, context.BoltCrates.DestroyedCrateCount);
        Assert.Equal(broken.Pickups.Count, context.BoltPickups.OutstandingCount);
        foreach (var pickup in broken.Pickups)
            Assert.Equal(pickup.Value, context.BoltCrates.CollectPickup(pickup.PickupId));
        Assert.Equal(19, context.BoltCrates.CollectedBolts);
        Assert.Equal(0, context.BoltPickups.OutstandingCount);
    }

    [Fact]
    public void ReentryReusesAuthoredIdsWithoutSharingRetiredMobyOrRewardState()
    {
        var old = new Rac1WorldContext(0, Rac1PickupRandom.FromSeed(1440));
        var next = new Rac1WorldContext(0, Rac1PickupRandom.FromSeed(1440));
        var source = Crate();
        var retiredActor = old.BoltCrates.Register(source, RuntimeEntityState.FromAuthored(source));
        var liveActor = next.BoltCrates.Register(source, RuntimeEntityState.FromAuthored(source));
        Assert.Equal(retiredActor.Key, liveActor.Key);
        Assert.NotSame(retiredActor, liveActor);
        var damage = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.PlayerWrench,
            Rac1GameplayEntityRef.Moby(retiredActor.Key), 2, 0x10000);
        old.BoltCrates.CompleteDamage(old.MobyRuntime.DispatchDamage<Rac1BoltCrateDamageAdmission>(damage), 19);
        Assert.True(next.MobyRuntime.Require(source).IsActive);
        Assert.Equal(0, next.BoltCrates.DestroyedCrateCount);
        Assert.Equal(0, next.BoltPickups.OutstandingCount);
        Assert.NotSame(old.DamageTransport, next.DamageTransport);
    }

    private static RuntimeDynamicObject Crate()
    {
        byte[] raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0xc), 100);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10), 20);
        return new(ObpSourceGame.Rac1, 500, 17, null, "moby:500", "moby:17", new(new double[16]), [],
            [new(Rac1BoltCrate.InstancePayloadFormat, raw), new(Rac1BoltCrate.PVarPayloadFormat, new byte[0x100])]);
    }
}
