using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class Rac2CrateInteractionTests
{
    [Theory]
    [InlineData(0x00000001u, 1f, true)]
    [InlineData(0x00010000u, 0.25f, true)]
    [InlineData(0x01000001u, 1f, true)]
    [InlineData(0x00000000u, 1f, false)]
    [InlineData(0x01000000u, 1f, false)]
    [InlineData(0x00000001u, 0f, false)]
    [InlineData(0x00000001u, -1f, false)]
    public void Class500BreakPredicateMatchesLoadedState1(uint damageFlags, float damageHp, bool expected)
    {
        Assert.Equal(expected, Rac2CrateInteraction.ShouldBreakClass500(damageFlags, damageHp));
    }

    [Theory]
    [InlineData(0, Rac2Class500PostBreakRoute.Deactivate)]
    [InlineData(1, Rac2Class500PostBreakRoute.State6)]
    [InlineData(255, Rac2Class500PostBreakRoute.State6)]
    public void AuthoredC8SelectsTheRetailPostBreakRoute(byte c8, Rac2Class500PostBreakRoute expected)
    {
        Assert.Equal(expected, Rac2CrateInteraction.PostBreakRoute(c8));
    }

    [Fact]
    public void NaNDoesNotPassNativePositiveDamageComparison()
    {
        Assert.False(Rac2CrateInteraction.ShouldBreakClass500(1, float.NaN));
        Assert.True(Rac2CrateInteraction.ShouldBreakClass500(1, float.PositiveInfinity));
        Assert.Equal(3, Rac2CrateInteraction.BreakTransitionState);
    }
}
