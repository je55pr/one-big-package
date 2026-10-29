using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class EffectReplayScriptTests
{
    [Theory]
    [InlineData(null, 65)]
    [InlineData("", 65)]
    [InlineData("65", 65)]
    [InlineData("140", 140)]
    [InlineData("180", 180)]
    [InlineData("600", 600)]
    public void AcceptsDefaultAndBoundedStopFrames(string? value, int expected) =>
        Assert.Equal(expected, EffectReplayScript.ParseStopFrame(value));

    [Theory]
    [InlineData("0")]
    [InlineData("64")]
    [InlineData("601")]
    [InlineData("-10")]
    [InlineData("invalid")]
    public void RejectsInvalidStopFrames(string value) =>
        Assert.Throws<InvalidOperationException>(() => EffectReplayScript.ParseStopFrame(value));
}
