using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class Rac2BoltRewardTests
{
    [Theory]
    [InlineData(13, 100, 13)]
    [InlineData(13, 50, 6)]
    [InlineData(14, 150, 21)]
    [InlineData(1000, 255, 2550)]
    public void AuthoredBoltValueUsesNativePercentageIntegerScaling(int authoredValue, int percentage, int expected)
    {
        Assert.Equal(expected, Rac2BoltReward.ScaleAuthoredValue(authoredValue, percentage));
    }

    [Fact]
    public void SelectorBlocksUseRecoveredRuntimeContextStride()
    {
        Assert.Equal(0x0019B4A8u, Rac2BoltReward.PackedSelectorTableAddress(0));
        Assert.Equal(0x0019B8A8u, Rac2BoltReward.PackedSelectorTableAddress(1));
        Assert.Equal(0x0019BCA8u, Rac2BoltReward.PackedSelectorTableAddress(2));
    }

    [Fact]
    public void RetailPercentageBanksMatchRecoveredLiteralTables()
    {
        Assert.Equal(new byte[] { 100, 50, 40, 30, 25, 20, 15, 10 }, Rac2BoltReward.AuthoredBank0.ToArray());
        Assert.Equal(Enumerable.Repeat((byte)100, 8), Rac2BoltReward.AuthoredBank1.ToArray());
        Assert.Equal(new byte[] { 100, 50, 40, 30, 25, 20, 15, 10 }, Rac2BoltReward.EmissionBank0.ToArray());
        Assert.Equal(new byte[] { 100, 30, 10, 10, 10, 10, 10, 10 }, Rac2BoltReward.EmissionBank1.ToArray());
    }

    [Theory]
    [InlineData(0xA3, 0, 3)]
    [InlineData(0xA3, 1, 10)]
    [InlineData(0x70, 0, 0)]
    [InlineData(0x70, 1, 7)]
    public void PackedSelectorUsesUidParityForLowAndHighNibbles(int packed, int uid, int expected)
    {
        Assert.Equal(expected, Rac2BoltReward.ReadPackedSelector([(byte)packed], uid));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 50)]
    [InlineData(7, 10)]
    [InlineData(8, 100)]
    [InlineData(15, 100)]
    public void AuthoredRewardSelectorChoosesRetailBankAndIndex(int selector, int expectedPercentage)
    {
        Assert.Equal(expectedPercentage, Rac2BoltReward.AuthoredPercentageForSelector(selector));
    }

    [Theory]
    [InlineData(13, 0, 13)]
    [InlineData(13, 1, 6)]
    [InlineData(13, 7, 1)]
    [InlineData(13, 8, 13)]
    public void AuthoredRewardCanScaleDirectlyFromNativeSelector(int authoredValue, int selector, int expected)
    {
        Assert.Equal(expected, Rac2BoltReward.ScaleAuthoredValueForSelector(authoredValue, selector));
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(10, 1, 3)]
    [InlineData(11, 0, 1)]
    [InlineData(20, 1, 2)]
    [InlineData(21, 0, 0)]
    [InlineData(999, 1, 1)]
    public void PhysicalPieceBudgetMatchesLoadedThresholds(int progressionLikeInput, int rngMod2, int expected)
    {
        Assert.Equal(expected, Rac2BoltReward.PhysicalPieceBudget(progressionLikeInput, rngMod2));
    }

    [Fact]
    public void ZeroPieceBudgetRoutesEntireValueToDeferredReservoir()
    {
        var plan = Rac2BoltReward.PartitionPhysicalPieces(1681, 0);
        Assert.Empty(plan.PhysicalDenominations);
        Assert.Equal(1681, plan.DeferredValue);
        Assert.Equal(1681, plan.TotalValue);
    }

    [Theory]
    [InlineData(1681, 3, new[] { 1000, 500, 100 }, 81)]
    [InlineData(1681, 7, new[] { 1000, 500, 100, 50, 20, 5, 5 }, 1)]
    [InlineData(1681, 8, new[] { 1000, 500, 100, 50, 20, 5, 5, 1 }, 0)]
    [InlineData(105, 2, new[] { 100, 5 }, 0)]
    [InlineData(3, 2, new[] { 1, 1 }, 1)]
    public void PartitionMatchesNativeGreedyDenominationOrder(
        int rewardValue, int maxPieces, int[] expectedPhysical, int expectedDeferred)
    {
        var plan = Rac2BoltReward.PartitionPhysicalPieces(rewardValue, maxPieces);
        Assert.Equal(expectedPhysical, plan.PhysicalDenominations);
        Assert.Equal(expectedDeferred, plan.DeferredValue);
        Assert.Equal(rewardValue, plan.TotalValue);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(49, 1, 48)]
    [InlineData(50, 1, 49)]
    [InlineData(51, 2, 49)]
    [InlineData(100, 2, 98)]
    [InlineData(1681, 34, 1647)]
    public void DeferredReservoirReleasesCeilingOneFiftieth(int pool, int expectedReleased, int expectedRemaining)
    {
        var release = Rac2BoltReward.ReleaseDeferred(pool);
        Assert.Equal(expectedReleased, release.ReleasedValue);
        Assert.Equal(expectedRemaining, release.RemainingValue);
        Assert.Equal(pool, release.OriginalValue);
    }

    [Fact]
    public void InvalidNativeInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.ScaleAuthoredValue(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.ScaleAuthoredValue(1, 256));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.ReadPackedSelector([0], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.ReadPackedSelector([0], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.AuthoredPercentageForSelector(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.PhysicalPieceBudget(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.PartitionPhysicalPieces(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.PartitionPhysicalPieces(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac2BoltReward.ReleaseDeferred(-1));
    }
}
