using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>Deterministic frame selection for baked animation clips.</summary>
public class AnimationClockTests
{
    [Fact]
    public void TimeZero_IsFrameZero_ForEveryMode()
    {
        foreach (var mode in System.Enum.GetValues<LoopMode>())
        {
            Assert.Equal(0, AnimationClock.FrameAt(10, 30, 0, mode));
        }
    }

    [Fact]
    public void DegenerateClips_StayAtFrameZero()
    {
        Assert.Equal(0, AnimationClock.FrameAt(1, 30, 5, LoopMode.Loop));
        Assert.Equal(0, AnimationClock.FrameAt(10, 0, 5, LoopMode.Loop));
    }

    [Theory]
    [InlineData(0.10, 3)]   // 0.10 s * 30 fps = 3.0
    [InlineData(0.333, 9)]  // 9.99 -> 9
    [InlineData(0.5, 5)]    // 15 -> wraps in a 10-frame clip
    [InlineData(1.0, 0)]    // 30 -> back to 0
    public void Loop_WrapsModuloFrameCount(double t, int expected)
    {
        Assert.Equal(expected, AnimationClock.FrameAt(10, 30, t, LoopMode.Loop));
    }

    [Fact]
    public void HoldLast_ClampsAtTheFinalFrame()
    {
        Assert.Equal(4, AnimationClock.FrameAt(5, 10, 0.4, LoopMode.HoldLast));  // 4.0
        Assert.Equal(4, AnimationClock.FrameAt(5, 10, 2.0, LoopMode.HoldLast));  // 20 -> clamp
    }

    [Fact]
    public void PingPong_BouncesOffBothEnds()
    {
        // 4-frame clip, period 6: 0 1 2 3 2 1 | 0 1 2 3 ...
        int[] want = { 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 2, 1 };
        for (int frame = 0; frame < want.Length; frame++)
        {
            double t = frame / 10.0; // 10 fps
            Assert.Equal(want[frame], AnimationClock.FrameAt(4, 10, t, LoopMode.PingPong));
        }
    }

    [Fact]
    public void SameTime_SameFrame()
    {
        Assert.Equal(
            AnimationClock.FrameAt(17, 24, 3.1415, LoopMode.PingPong),
            AnimationClock.FrameAt(17, 24, 3.1415, LoopMode.PingPong));
    }
}
