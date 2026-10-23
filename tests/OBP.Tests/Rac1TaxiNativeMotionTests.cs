using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1TaxiNativeMotionTests
{
    private static Rac1TaxiPoint P(double x) => new(x, 0, 0);

    [Fact]
    public void NativeSeekAcceleratesAndBrakesOnAuthoredPath()
    {
        var motion = new Rac1TaxiNativeMotion([P(0), P(10), P(20)]);
        Assert.Equal(P(0), motion.Position);
        Assert.Equal(0, motion.Progress);
        Assert.Equal(0, motion.SpeedPerFrame);

        double before = 0;
        for (int i = 0; i < 10; i++)
        {
            motion.Advance(1d / 60);
            Assert.True(motion.Position.X > before);
            Assert.InRange(motion.SpeedPerFrame, 0, .3);
            before = motion.Position.X;
        }
        Assert.True(motion.SpeedPerFrame > .00125);
        Assert.False(motion.Arrived);

        double progress = motion.Progress;
        int frames = 10;
        while (!motion.Arrived && frames++ < 60 * 20)
        {
            motion.Advance(1d / 60);
            Assert.True(motion.Progress >= progress - 1e-10);
            progress = motion.Progress;
        }
        Assert.True(motion.Arrived);
        Assert.InRange(frames, 40, 60 * 20);
        Assert.Equal(P(20), motion.Position);
        Assert.Equal(1, motion.Progress);
        motion.Advance(100);
        Assert.Equal(P(20), motion.Position);
    }

    [Fact]
    public void RetailSixtyHertzSteppingIsIndependentOfHostPhysicsRate()
    {
        Rac1TaxiPoint[] path = [P(0), P(5), P(13), P(25)];
        var native = new Rac1TaxiNativeMotion(path);
        var halfRate = new Rac1TaxiNativeMotion(path);
        var doubleRate = new Rac1TaxiNativeMotion(path);
        for (int i = 0; i < 600; i++)
        {
            native.Advance(1d / 60);
            if (i % 2 == 0)
                halfRate.Advance(1d / 30);
            doubleRate.Advance(1d / 120);
            doubleRate.Advance(1d / 120);
        }

        Assert.Equal(native.Arrived, halfRate.Arrived);
        Assert.Equal(native.Arrived, doubleRate.Arrived);
        Assert.Equal(native.WaypointIndex, halfRate.WaypointIndex);
        Assert.Equal(native.WaypointIndex, doubleRate.WaypointIndex);
        Assert.Equal(native.Position.X, halfRate.Position.X, 7);
        Assert.Equal(native.Position.X, doubleRate.Position.X, 7);
    }

    [Fact]
    public void NativeStepperRejectsInvalidDataWithoutPoisoningItsState()
    {
        Assert.Throws<ArgumentException>(() =>
            new Rac1TaxiNativeMotion([P(1), P(1)]));
        Assert.Throws<ArgumentException>(() =>
            new Rac1TaxiNativeMotion([P(0), P(double.NaN)]));
        var motion = new Rac1TaxiNativeMotion([P(0), P(15)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => motion.Advance(-1));
        Assert.Equal(P(0), motion.Position);
    }
}
