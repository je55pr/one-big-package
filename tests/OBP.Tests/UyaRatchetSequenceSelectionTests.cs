using OBP.RAC3.Player;

namespace OBP.Tests;

public sealed class UyaRatchetSequenceSelectionTests
{
    [Fact]
    public void OrdinaryIdleAndLocomotionPreserveRecoveredSelectors()
    {
        Assert.Equal(127, UyaRatchetSequenceSelection.SequenceCount);
        Assert.Equal(0, UyaRatchetSequenceSelection.NeutralIdleSequenceId);
        Assert.Equal(new[] { 3, 4 }, UyaRatchetSequenceSelection.LocomotionFamily);
        Assert.Equal(new[] { 5, 6 }, UyaRatchetSequenceSelection.LocomotionStopFamily);

        Assert.Equal(3, UyaRatchetSequenceSelection.SelectOrdinaryLocomotionSequence(0));
        Assert.Equal(4, UyaRatchetSequenceSelection.SelectOrdinaryLocomotionSequence(1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UyaRatchetSequenceSelection.SelectOrdinaryLocomotionSequence(2));
    }

    [Theory]
    [InlineData(0.0f, 10)]
    [InlineData(1.75f, 10)]
    [InlineData(1.7501f, 11)]
    public void OrdinaryFallUsesRecoveredNativeMetricBoundary(float metric, int expected)
    {
        Assert.Equal(expected, UyaRatchetSequenceSelection.SelectOrdinaryFallSequence(metric));
        Assert.Equal(6, UyaRatchetSequenceSelection.OrdinaryFallNativeState);
        Assert.False(UyaRatchetSequenceSelection.HasRecoveredUniversalJumpSequence);
    }

    [Theory]
    [InlineData(23, 32)]
    [InlineData(24, 33)]
    [InlineData(25, 36)]
    [InlineData(26, 37)]
    public void DamageStatesSelectRecoveredReactionSequences(int nativeState, int expected)
    {
        Assert.Equal(expected, UyaRatchetSequenceSelection.SelectDamageSequence(nativeState));
    }

    [Fact]
    public void CombatStatesKeepRecoveredSequenceFamilies()
    {
        Assert.Equal(new[] { 23, 24, 25 }, UyaRatchetSequenceSelection.State19ComboFamily);
        Assert.Equal(23, UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(19, 0));
        Assert.Equal(24, UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(19, 1));
        Assert.Equal(25, UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(19, 2));
        Assert.Equal(40, UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(20));
        Assert.Equal(26, UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(21));
        Assert.Null(UyaRatchetSequenceSelection.SelectRecoveredCombatSequence(22));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UyaRatchetSequenceSelection.SelectState19ComboSequence(3));
    }

    [Theory]
    [InlineData(118, 90)]
    [InlineData(123, 11)]
    [InlineData(127, 17)]
    public void DeathStatesSelectRecoveredPresentationSequences(int nativeState, int expected)
    {
        Assert.Equal(expected, UyaRatchetSequenceSelection.SelectDeathSequence(nativeState));
    }

    [Fact]
    public void UnprovenPresentationBoundariesRemainExplicit()
    {
        Assert.False(UyaRatchetSequenceSelection.HasRecoveredDedicatedStrafeSequence);
        Assert.False(UyaRatchetSequenceSelection.HasRecoveredContextualIdleCycle);
        Assert.False(UyaRatchetSequenceSelection.HasDecodedRatchetPoseFrames);
        Assert.Null(UyaRatchetSequenceSelection.SelectDamageSequence(22));
        Assert.Null(UyaRatchetSequenceSelection.SelectDeathSequence(57));
    }
}
