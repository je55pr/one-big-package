using System.Buffers.Binary;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class Rac1BoltCrateTests
{
    [Fact]
    public void ReadsAuthoredUidRewardCentreAndNativeRange()
    {
        var source = Class500(uid: 121, rewardCentre: 10);

        var authored = Assert.IsType<Rac1BoltCrateAuthoredState>(Rac1BoltCrate.ReadAuthored(source));
        Assert.Equal(121, authored.Uid);
        Assert.Equal(10, authored.RewardCentre);
        Assert.Equal(0x100, authored.PVarSize);

        var range = Rac1BoltCrate.RewardRange(authored.RewardCentre);
        Assert.Equal(new Rac1BoltRewardRange(10, 3, 7, 13), range);
        Assert.True(range.Contains(7));
        Assert.True(range.Contains(13));
        Assert.False(range.Contains(6));
        Assert.False(range.Contains(14));
    }

    [Theory]
    [InlineData(-1.0, false)]
    [InlineData(0.0, false)]
    [InlineData(0.0001, true)]
    public void PositiveNativeDamageIsStrictBreakTrigger(double damage, bool expected)
    {
        Assert.Equal(expected, Rac1BoltCrate.ShouldBreak(damage));
    }

    [Theory]
    [InlineData(7, new[] { 5, 1, 1 })]
    [InlineData(9, new[] { 5, 1, 1, 1, 1 })]
    [InlineData(12, new[] { 5, 5, 1, 1 })]
    [InlineData(19, new[] { 5, 5, 5, 1, 1, 1, 1 })]
    public void RepresentativePartitionMatchesRetailLowValueBranch(int total, int[] expected)
    {
        Assert.Equal(expected, Rac1BoltCrate.PartitionRepresentativeTotal(total));
    }

    [Fact]
    public void BreakProjectsInactiveAndCollectionCreditsExactDenominations()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var runtime = new Rac1MobyRuntimeSession();
        var persistence = new Rac1MobyPersistenceSession(levelId: 0);
        var session = new Rac1BoltCrateSession(runtime, persistence);
        var initial = RuntimeEntityState.FromAuthored(source);
        var registered = session.Register(source, initial);

        var result = Assert.IsType<Rac1BoltCrateBreakResult>(
            session.ApplyDamage(source, initial, nativeDamage: 1, selectedTotal: 12));

        Assert.Equal(Rac1BoltCrate.ActiveNativeState, result.NativeStateBefore);
        Assert.Equal(Rac1BoltCrate.BreakTransitionNativeState, result.NativeBreakTransitionState);
        Assert.Equal(Rac1BoltCrate.DisabledNativeState, result.NativeDisabledState);
        Assert.Equal(RuntimeEntityPresence.Inactive, result.EntityState.Presentation.Presence);
        Assert.Equal(initial.Identity, result.EntityState.Identity);
        Assert.Same(registered, runtime.Require(source));
        Assert.Equal(Rac1BoltCrate.DisabledNativeState, registered.State.NativeState);
        Assert.Equal(RuntimeEntityPresence.Inactive, registered.Presence);
        Assert.Equal(12, result.PhysicalValue);
        Assert.Equal(new[] { 14, 14, 13, 13 }, result.Pickups.Select(p => p.NativeClassId));

        foreach (var pickup in result.Pickups) session.CollectPickup(pickup.PickupId);
        Assert.Equal(12, session.CollectedBolts);
        Assert.Equal(0, session.OutstandingPickupCount);
        Assert.Throws<InvalidOperationException>(() => session.CollectPickup(result.Pickups[0].PickupId));
    }

    [Fact]
    public void ZeroDamageDoesNotMutateSessionOrEntity()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var session = new Rac1BoltCrateSession();
        var initial = RuntimeEntityState.FromAuthored(source);

        Assert.Null(session.ApplyDamage(source, initial, nativeDamage: 0, selectedTotal: 12));
        Assert.Equal(0, session.DestroyedCrateCount);
        Assert.Equal(0, session.OutstandingPickupCount);
        Assert.Equal(RuntimeEntityPresence.Active, initial.Presentation.Presence);
    }

    [Fact]
    public void InvalidRngSelectionAndDuplicateUidDoNotDoublePay()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var session = new Rac1BoltCrateSession();
        var initial = RuntimeEntityState.FromAuthored(source);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.ApplyDamage(source, initial, nativeDamage: 1, selectedTotal: 14));
        Assert.Equal(0, session.DestroyedCrateCount);

        var first = Assert.IsType<Rac1BoltCrateBreakResult>(
            session.ApplyDamage(source, initial, nativeDamage: 1, selectedTotal: 7));
        Assert.Equal(7, first.PhysicalValue);
        Assert.Throws<InvalidOperationException>(() =>
            session.ApplyDamage(source, initial, nativeDamage: 1, selectedTotal: 8));
    }

    [Fact]
    public void GenericDamageEventRoutesToCrateConsequenceAndValidatesVictimIdentity()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var persistence = new Rac1MobyPersistenceSession(levelId: 0);
        var session = new Rac1BoltCrateSession(persistence);
        var initial = RuntimeEntityState.FromAuthored(source);
        var wrench = new Rac1WrenchDamageResult(
            Rac1WrenchContactPath.HostPolicyAdmission,
            Rac1WrenchCombatController.RepresentativeDamage,
            Rac1WrenchCombatController.RepresentativeDamageFlags);
        var damage = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(
                source.NativeClassId,
                source.InstanceIndex),
            wrench);

        var result = Assert.IsType<Rac1BoltCrateBreakResult>(
            session.ApplyDamage(
                source,
                initial,
                damage,
                selectedTotal: 7));

        Assert.Equal(7, result.PhysicalValue);
        Assert.Equal(
            Rac1MobyUidPersistenceBits.BothSet,
            persistence.QueryUid(source));

        var wrongTarget = Rac1DamageRuntime.FromWrench(
            new Rac1MobyRuntimeKey(
                source.NativeClassId,
                source.InstanceIndex + 1),
            wrench);
        Assert.Throws<ArgumentException>(() =>
            new Rac1BoltCrateSession().ApplyDamage(
                source,
                initial,
                wrongTarget,
                selectedTotal: 7));
    }

    [Fact]
    public void SameWorldControllerRecreationPreservesDestroyedUidState()
    {
        var source = Class500(uid: 121, rewardCentre: 10);
        var persistence = new Rac1MobyPersistenceSession(levelId: 0);
        var firstSession = new Rac1BoltCrateSession(persistence);
        var initial = RuntimeEntityState.FromAuthored(source);

        _ = Assert.IsType<Rac1BoltCrateBreakResult>(
            firstSession.ApplyDamage(
                source,
                initial,
                nativeDamage: 1,
                selectedTotal: 7));

        var restartedController = new Rac1BoltCrateSession(persistence);

        Assert.Equal(1, restartedController.DestroyedCrateCount);
        Assert.Equal(
            Rac1MobyUidPersistenceBits.BothSet,
            persistence.QueryUid(source));
        Assert.Throws<InvalidOperationException>(() =>
            restartedController.ApplyDamage(
                source,
                initial,
                nativeDamage: 1,
                selectedTotal: 8));
    }

    [Fact]
    public void UnadmittedRewardCentreDoesNotInventRngRange()
    {
        Assert.Throws<NotSupportedException>(() => Rac1BoltCrate.RewardRange(15));
    }

    [Fact]
    public void RetailPickupClassesMapToOneFiveTwentyFifty()
    {
        Assert.Equal(1, Rac1BoltCrate.ValueForNativePickupClass(13));
        Assert.Equal(5, Rac1BoltCrate.ValueForNativePickupClass(14));
        Assert.Equal(20, Rac1BoltCrate.ValueForNativePickupClass(15));
        Assert.Equal(50, Rac1BoltCrate.ValueForNativePickupClass(16));
    }

    private static RuntimeDynamicObject Class500(int uid, int rewardCentre)
    {
        var raw = new byte[0x78];
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x0c, 2), checked((ushort)uid));
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), rewardCentre);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x18, 4), 500);
        var pvar = new byte[0x100];

        return new RuntimeDynamicObject(
            "rac1", 500, 89, uid, "moby:500", "moby:89",
            new RuntimeObjectTransform(new double[16]), Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload(Rac1BoltCrate.InstancePayloadFormat, raw),
             new RuntimeOpaquePayload(Rac1BoltCrate.PVarPayloadFormat, pvar)]);
    }
}
