using OBP.RAC1.Animation;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class Rac1MobyAnimationProviderTests
{
    [Fact]
    public void RedPlantSelectorRequiresNearbyMovingPlayer()
    {
        var rest = RuntimeObjectAnimationRole.Rest;
        Assert.Equal(rest, Select(rest, 1.0, 1.0));
        Assert.Equal(rest, Select(rest, 0.999, 1.0 / 30.0));
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, Select(rest, 0.999, 1.0 / 30.0 + 1e-6));
    }

    [Fact]
    public void RedPlantReturnFlagWinsAfterActivationCheck()
    {
        Assert.Equal(RuntimeObjectAnimationRole.Rest,
            Select(RuntimeObjectAnimationRole.Reaction, 0.2, 0.1,
                Rac1MobyAnimationProvider.RedPlantReturnToRestFlag));
        Assert.Equal(RuntimeObjectAnimationRole.Rest,
            Select(RuntimeObjectAnimationRole.Rest, 0.2, 0.1,
                Rac1MobyAnimationProvider.RedPlantReturnToRestFlag));
    }

    [Fact]
    public void SelectorConstantsMatchRecoveredRetailThresholds()
    {
        Assert.Equal(1.0, Rac1MobyAnimationProvider.RedPlantTriggerDistance);
        Assert.Equal(1.0 / 30.0, Rac1MobyAnimationProvider.RedPlantMinimumPlayerMotionPerTick, 12);
    }

    private static RuntimeObjectAnimationRole Select(
        RuntimeObjectAnimationRole role, double distance, double motion, byte flags = 0) =>
        Rac1MobyAnimationProvider.SelectRedPlantRole(
            new Rac1MobyAnimationProvider.RedPlantSelectorObservation(role, distance, motion, flags));
}
