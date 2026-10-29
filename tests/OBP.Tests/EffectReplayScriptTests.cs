using OBP.Game.Diagnostics;
using OBP.Runtime.Diagnostics;

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
    [InlineData(65)]
    [InlineData(600)]
    public void RunsNativeWindowsAndStopsExactlyAtTheConfiguredFrame(int stopFrame)
    {
        var commands = new List<PlaytestCommand>();
        int stops = 0;
        for (int frame = 1; frame <= stopFrame + 1; frame++)
        {
            EffectReplayScript.Apply(frame, commands.Add, () => stops++, stopFrame);
            Assert.Equal(frame >= stopFrame ? 1 : 0, stops);
        }
        Assert.Equal(new[]
        {
            new PlaytestCommand("effects", "start"),
            new PlaytestCommand("effects", "reset"),
            new PlaytestCommand("effects", "start"),
        }, commands);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("64")]
    [InlineData("601")]
    [InlineData("-10")]
    [InlineData("invalid")]
    public void RejectsInvalidStopFrames(string value) =>
        Assert.Throws<InvalidOperationException>(() => EffectReplayScript.ParseStopFrame(value));
}
