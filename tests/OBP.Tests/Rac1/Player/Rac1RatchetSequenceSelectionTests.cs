using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetSequenceSelectionTests
{
    [Fact]
    public void NeutralAndLocomotionSelectionPreserveRecoveredFamilies()
    {
        Assert.Equal(
            new[] { 0, 2, 0, 1 },
            Rac1RatchetSequenceSelection.NeutralIdleCycle);
        Assert.Equal(
            new[] { 3, 4 },
            Rac1RatchetSequenceSelection.LocomotionEntry);
        Assert.Equal(
            new[] { 5, 20, 6 },
            Rac1RatchetSequenceSelection.LocomotionStopFamily);
        Assert.True(Rac1RatchetSequenceSelection.OrdinaryTurningUsesLocomotionFamily);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 8)]
    public void JumpSelectorDependsOnlyOnRecoveredLaunchContext(
        bool movingAtLaunch,
        int expectedSequence)
    {
        Assert.Equal(
            expectedSequence,
            Rac1RatchetSequenceSelection.SelectJumpSequence(movingAtLaunch));
        Assert.False(Rac1RatchetSequenceSelection.HasDistinctJumpAnticipationSequence);
        Assert.False(Rac1RatchetSequenceSelection.HasDistinctApexOrFallSequence);
        Assert.False(Rac1RatchetSequenceSelection.HasDistinctLandingSequence);
    }

    [Theory]
    [InlineData(Rac1RatchetSequenceSelection.CrouchTurnDirection.None, 13)]
    [InlineData(Rac1RatchetSequenceSelection.CrouchTurnDirection.Right, 14)]
    [InlineData(Rac1RatchetSequenceSelection.CrouchTurnDirection.Left, 15)]
    public void CrouchSelectionKeepsDirectionSpecificRetailSequences(
        Rac1RatchetSequenceSelection.CrouchTurnDirection direction,
        int expectedSequence)
    {
        Assert.Equal(
            expectedSequence,
            Rac1RatchetSequenceSelection.SelectCrouchSequence(direction));
    }

    [Fact]
    public void CombatSelectionSeparatesProvenFireFromUnresolvedDamageReaction()
    {
        Assert.Equal(0x13, Rac1RatchetSequenceSelection.WrenchActionState);
        Assert.Equal(23, Rac1RatchetSequenceSelection.WrenchAttackSequenceId);
        Assert.Equal(
            44,
            Rac1RatchetSequenceSelection.SelectFirstRangedFireSequence(fireAccepted: true));
        Assert.Null(
            Rac1RatchetSequenceSelection.SelectFirstRangedFireSequence(fireAccepted: false));
        Assert.Null(Rac1RatchetSequenceSelection.DamageReactionSequenceId);
        Assert.Null(Rac1RatchetSequenceSelection.CombatDeathSequenceId);
    }

    [Fact]
    public void EnvironmentalDeathKeepsObservedTwoSequencePathAndTerminalState()
    {
        Assert.Equal(
            new[] { 10, 11 },
            Rac1RatchetSequenceSelection.EnvironmentalDeathSequencePath);
        Assert.Equal(
            Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence,
            Rac1RatchetSequenceSelection.EnvironmentalDeathTerminalSequenceId);
        Assert.Equal(
            Rac1RatchetNanotechSession.RetailVeldinDeathNativeState,
            Rac1RatchetSequenceSelection.VeldinEnvironmentalDeathTerminalNativeState);
    }
}
