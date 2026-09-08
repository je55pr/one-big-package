using System.Buffers.Binary;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public class GcFreshBoltSessionTests
{
    [Fact]
    public void FreshShowcasePayoutUsesExplicitSelectorZero()
    {
        var session = new GcFreshBoltSession();

        var payout = session.PlanClass500Payout(
            uid: 42, authoredBolts: 13, rewardMultiplierByte: 0,
            progressionLikeInput: 0, rngMod2: 1);

        Assert.Equal(0, payout.Selector);
        Assert.Equal(100, payout.Percentage);
        Assert.Equal(13, payout.PercentageScaledValue);
        Assert.Equal(0, payout.RewardMultiplierByte);
        Assert.Equal(1, payout.EffectiveMultiplier);
        Assert.Equal(13, payout.ScaledRewardValue);
        Assert.Equal(0, payout.ReleasedDeferredValue);
        Assert.Equal(13, payout.RewardCentreValue);
        Assert.Equal(3, payout.PhysicalPieceBudget);
        Assert.Equal(new[] { 5, 5, 1 }, payout.PhysicalPickups.Select(p => p.Denomination));
        Assert.Equal(2, payout.DeferredValue);
        Assert.Equal(2, session.DeferredBolts);
    }

    [Fact]
    public void RewardMultiplierByteUsesRecoveredMaxOneRule()
    {
        var neutral = new GcFreshBoltSession().PlanClass500Payout(1, 13, 0, 0, 1);
        var doubled = new GcFreshBoltSession().PlanClass500Payout(1, 13, 2, 0, 1);

        Assert.Equal(1, neutral.EffectiveMultiplier);
        Assert.Equal(13, neutral.ScaledRewardValue);
        Assert.Equal(2, doubled.EffectiveMultiplier);
        Assert.Equal(26, doubled.ScaledRewardValue);
        Assert.Equal(new[] { 20, 5, 1 }, doubled.PhysicalPickups.Select(p => p.Denomination));
        Assert.Equal(0, doubled.DeferredValue);
    }

    [Fact]
    public void NextRewardReleasesRecoveredFractionOfDeferredReservoir()
    {
        var session = new GcFreshBoltSession();
        session.PlanClass500Payout(1, 13, 0, 0, 1);

        var second = session.PlanClass500Payout(2, 13, 0, 0, 1);

        Assert.Equal(1, second.ReleasedDeferredValue);
        Assert.Equal(14, second.RewardCentreValue);
        Assert.Equal(3, second.DeferredValue);
        Assert.Equal(4, session.DeferredBolts);
    }

    [Fact]
    public void CollectingPhysicalPickupsCreditsOnlyTheirDenominations()
    {
        var session = new GcFreshBoltSession();
        var payout = session.PlanClass500Payout(7, 13, 0, 0, 1);

        Assert.Equal(5, session.CollectPickup(payout.PhysicalPickups[0].PickupId));
        Assert.Equal(5, session.CollectedBolts);
        Assert.Equal(2, session.OutstandingPickupCount);
        Assert.Equal(2, session.DeferredBolts);

        Assert.Equal(5, session.CollectPickup(payout.PhysicalPickups[1].PickupId));
        Assert.Equal(1, session.CollectPickup(payout.PhysicalPickups[2].PickupId));
        Assert.Equal(11, session.CollectedBolts);
        Assert.Equal(0, session.OutstandingPickupCount);
        Assert.Equal(2, session.DeferredBolts);
    }

    [Fact]
    public void SameUidCannotPayTwiceInOneFreshSession()
    {
        var session = new GcFreshBoltSession();
        session.PlanClass500Payout(99, 13, 0, 0, 0);

        Assert.Throws<InvalidOperationException>(() => session.PlanClass500Payout(99, 13, 0, 0, 0));
    }

    [Fact]
    public void AuthorityReaderExtractsGcPackedFieldsAwayFromGodot()
    {
        var raw = new byte[0x88];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x10, 4), 1234);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x14, 4), 13);
        var pvar = new byte[0xD0];
        pvar[0xC8] = 7;

        var source = new RuntimeDynamicObject(
            "rac2", 500, 3, 1234, "moby:500", "level:1:moby:3",
            new RuntimeObjectTransform(new double[16]),
            Array.Empty<RuntimeObjectMesh>(),
            [new RuntimeOpaquePayload("rac2-moby-instance-0x88", raw),
             new RuntimeOpaquePayload("rac2-pvar", pvar)]);

        var state = Assert.IsType<GcClass500AuthoredState>(GcClass500Authority.Read(source));
        Assert.Equal(1234, state.Uid);
        Assert.Equal(13, state.AuthoredBolts);
        Assert.Equal((byte?)7, state.PvarC8);
    }
}
