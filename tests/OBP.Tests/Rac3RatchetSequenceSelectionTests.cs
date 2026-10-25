using OBP.RAC3.Player;

namespace OBP.Tests;

public sealed class Rac3RatchetSequenceSelectionTests
{
    [Fact]
    public void OrdinaryIdleAndLocomotionPreserveRecoveredSelectors()
    {
        Assert.Equal(127, Rac3RatchetSequenceSelection.SequenceCount);
        Assert.Equal(0, Rac3RatchetSequenceSelection.NeutralIdleSequenceId);
        Assert.Equal(new[] { 3, 4 }, Rac3RatchetSequenceSelection.LocomotionFamily);
        Assert.Equal(new[] { 5, 6 }, Rac3RatchetSequenceSelection.LocomotionStopFamily);

        Assert.Equal(3, Rac3RatchetSequenceSelection.SelectOrdinaryLocomotionSequence(0));
        Assert.Equal(4, Rac3RatchetSequenceSelection.SelectOrdinaryLocomotionSequence(1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rac3RatchetSequenceSelection.SelectOrdinaryLocomotionSequence(2));
    }

    [Theory]
    [InlineData(0.0f, 10)]
    [InlineData(1.75f, 10)]
    [InlineData(1.7501f, 11)]
    public void OrdinaryFallUsesRecoveredNativeMetricBoundary(float metric, int expected)
    {
        Assert.Equal(expected, Rac3RatchetSequenceSelection.SelectOrdinaryFallSequence(metric));
        Assert.Equal(6, Rac3RatchetSequenceSelection.OrdinaryFallNativeState);
        Assert.False(Rac3RatchetSequenceSelection.HasRecoveredUniversalJumpSequence);
    }

    [Theory]
    [InlineData(23, 32)]
    [InlineData(24, 33)]
    [InlineData(25, 36)]
    [InlineData(26, 37)]
    public void DamageStatesSelectRecoveredReactionSequences(int nativeState, int expected)
    {
        Assert.Equal(expected, Rac3RatchetSequenceSelection.SelectDamageSequence(nativeState));
    }

    [Fact]
    public void CombatStatesKeepRecoveredSequenceFamilies()
    {
        Assert.Equal(new[] { 23, 24, 25 }, Rac3RatchetSequenceSelection.State19ComboFamily);
        Assert.Equal(23, Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(19, 0));
        Assert.Equal(24, Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(19, 1));
        Assert.Equal(25, Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(19, 2));
        Assert.Equal(40, Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(20));
        Assert.Equal(26, Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(21));
        Assert.Null(Rac3RatchetSequenceSelection.SelectRecoveredCombatSequence(22));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rac3RatchetSequenceSelection.SelectState19ComboSequence(3));
    }

    [Theory]
    [InlineData(118, 90)]
    [InlineData(123, 11)]
    [InlineData(127, 17)]
    public void DeathStatesSelectRecoveredPresentationSequences(int nativeState, int expected)
    {
        Assert.Equal(expected, Rac3RatchetSequenceSelection.SelectDeathSequence(nativeState));
    }

    [Fact]
    public void UnprovenPresentationBoundariesRemainExplicit()
    {
        Assert.False(Rac3RatchetSequenceSelection.HasRecoveredDedicatedStrafeSequence);
        Assert.False(Rac3RatchetSequenceSelection.HasRecoveredContextualIdleCycle);
        Assert.False(Rac3RatchetSequenceSelection.HasDecodedRatchetPoseFrames);
        Assert.Null(Rac3RatchetSequenceSelection.SelectDamageSequence(22));
        Assert.Null(Rac3RatchetSequenceSelection.SelectDeathSequence(57));
    }
}
