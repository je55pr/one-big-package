using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeYawSeekTests
{
    private const double Frame = 1d / Rac1TaxiNativeYawSeek.NativeFramesPerSecond;
    private static double Error(double target, double current) =>
        Math.Abs(Math.IEEERemainder(target - current, 2d * Math.PI));

    [Fact]
    public void RetailFirstFrameAcceleratesOneFortyFifthDegreeSecondStep()
    {
        var seek = new Rac1TaxiNativeYawSeek(0);
        seek.Advance(Math.PI / 2, Frame);
        double expected = (Math.PI / 4) / 60;
        Assert.Equal(expected, seek.HeadingRadians, 10);
        Assert.Equal(expected, seek.SpinRadiansPerFrame, 10);
        seek.Advance(Math.PI / 2, Frame);
        Assert.Equal(expected * 3, seek.HeadingRadians, 10);
        Assert.Equal(expected * 2, seek.SpinRadiansPerFrame, 10);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public void TurnsInBothNativeDirectionsAndBrakesAtTarget(double target)
    {
        var seek = new Rac1TaxiNativeYawSeek(0);
        seek.Advance(target, Frame);
        Assert.Equal(Math.Sign(target), Math.Sign(seek.HeadingRadians));
        Assert.Equal(Math.Sign(target), Math.Sign(seek.SpinRadiansPerFrame));
        for (int i = 0; i < 360; i++)
        {
            seek.Advance(target, Frame);
            Assert.True(Math.Abs(seek.SpinRadiansPerFrame) <=
                2d * Math.PI / 60d + 1e-9);
        }
        Assert.True(Error(target, seek.HeadingRadians) < 0.06,
            $"target={target}; heading={seek.HeadingRadians}");
    }

    [Fact]
    public void HeadingWraparoundUsesShortestDirectionAcrossPi()
    {
        var seek = new Rac1TaxiNativeYawSeek(Math.PI - 0.05);
        seek.Advance(-Math.PI + 0.05, Frame);
        Assert.True(seek.SpinRadiansPerFrame > 0);
        for (int i = 0; i < 120; i++)
            seek.Advance(-Math.PI + 0.05, Frame);
        Assert.True(Error(-Math.PI + 0.05, seek.HeadingRadians) < 0.04);
    }

    [Fact]
    public void PersistentAngularVelocityBrakesBeforeReverse()
    {
        var seek = new Rac1TaxiNativeYawSeek(0);
        for (int i = 0; i < 10; i++)
            seek.Advance(Math.PI, Frame);
        Assert.True(seek.SpinRadiansPerFrame > 0);
        double spin = seek.SpinRadiansPerFrame;
        seek.Advance(-Math.PI / 2, Frame);
        Assert.True(seek.SpinRadiansPerFrame >= 0);
        Assert.True(seek.SpinRadiansPerFrame < spin);
        bool reversed = false;
        for (int i = 0; i < 120; i++)
        {
            seek.Advance(-Math.PI / 2, Frame);
            reversed |= seek.SpinRadiansPerFrame < 0;
        }
        Assert.True(reversed);
        Assert.True(Error(-Math.PI / 2, seek.HeadingRadians) < 0.04);
    }

    [Fact]
    public void NativeSixtyHertzSteppingIsHostFrameRateIndependent()
    {
        var slow = new Rac1TaxiNativeYawSeek(0.35);
        var fast = new Rac1TaxiNativeYawSeek(0.35);
        // 30 Hz and 120 Hz hosts should yield the same sixty
        // original R&C1 updates, including persistent angular speed.
        for (int i = 0; i < 30; i++)
            slow.Advance(-1.3, Frame * 2);
        for (int i = 0; i < 120; i++)
            fast.Advance(-1.3, Frame / 2);
        Assert.Equal(slow.HeadingRadians, fast.HeadingRadians, 10);
        Assert.Equal(slow.SpinRadiansPerFrame, fast.SpinRadiansPerFrame, 10);
    }

    [Fact]
    public void RejectsInvalidSourceHeadingAndElapsedTimeWithoutAdvancing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Rac1TaxiNativeYawSeek(double.PositiveInfinity));
        var seek = new Rac1TaxiNativeYawSeek(0.2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            seek.Advance(double.NaN, Frame));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            seek.Advance(1, -Frame));
        Assert.Equal(0.2, seek.HeadingRadians, 10);
    }
}
