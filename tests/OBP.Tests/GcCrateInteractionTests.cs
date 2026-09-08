using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public class GcCrateInteractionTests
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
        Assert.Equal(expected, GcCrateInteraction.ShouldBreakClass500(damageFlags, damageHp));
    }

    [Theory]
    [InlineData(0, GcClass500PostBreakRoute.Deactivate)]
    [InlineData(1, GcClass500PostBreakRoute.State6)]
    [InlineData(255, GcClass500PostBreakRoute.State6)]
    public void AuthoredC8SelectsTheRetailPostBreakRoute(byte c8, GcClass500PostBreakRoute expected)
    {
        Assert.Equal(expected, GcCrateInteraction.PostBreakRoute(c8));
    }

    [Fact]
    public void NaNDoesNotPassNativePositiveDamageComparison()
    {
        Assert.False(GcCrateInteraction.ShouldBreakClass500(1, float.NaN));
        Assert.True(GcCrateInteraction.ShouldBreakClass500(1, float.PositiveInfinity));
        Assert.Equal(3, GcCrateInteraction.BreakTransitionState);
    }
}
