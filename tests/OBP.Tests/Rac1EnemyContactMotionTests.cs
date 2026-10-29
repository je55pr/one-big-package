using Godot;

namespace OBP.Tests;

public sealed class Rac1EnemyContactMotionTests
{
    [Theory]
    [InlineData(749, true, false, -0.2f, 0f)]
    [InlineData(749, false, false, -0.2f, -0.2f)]
    [InlineData(749, true, true, -0.2f, -0.2f)]
    [InlineData(749, true, false, 0.2f, 0.2f)]
    [InlineData(580, true, false, -0.2f, -0.2f)] // Released Sandshark must keep its downward launch.
    [InlineData(827, true, false, -0.2f, -0.2f)]
    [InlineData(1440, true, false, -0.2f, -0.2f)]
    public void OnlyGroundedOrdinaryClass749RemovesDownwardMotion(int nativeClass, bool grounded,
        bool reacting, float y, float expectedY)
    {
        var input = new Vector3(0.125f, y, -0.35f);
        var result = OBP.Game.Rac1EnemyContactMotion.PrepareOrdinary(nativeClass, grounded, reacting, input);
        Assert.Equal(new Vector3(input.X, expectedY, input.Z), result);
    }
}
