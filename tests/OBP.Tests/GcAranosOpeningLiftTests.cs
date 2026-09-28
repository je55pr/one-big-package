using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class GcAranosOpeningLiftTests
{
    [Fact]
    public void LowerIdleDoesNotMoveBeforeRiderAdmission()
    {
        var lift = new GcAranosOpeningLiftSession(Source());

        var step = lift.AdvanceNativeTick();

        Assert.Equal(GcAranosOpeningLiftPhase.LowerIdle, step.Phase);
        Assert.Equal(50.031, step.NativeZ, 6);
        Assert.Equal(0d, step.DeltaNativeZ);
        Assert.False(lift.TryBeginRise(riderPresent: false));
    }

    [Fact]
    public void RiderStartsRetailRateLimitedRise()
    {
        var lift = new GcAranosOpeningLiftSession(Source());
        Assert.True(lift.TryBeginRise(riderPresent: true));

        var first = lift.AdvanceNativeTick();

        Assert.Equal(GcAranosOpeningLiftPhase.Rising, first.Phase);
        Assert.Equal(GcAranosOpeningLiftSession.RiseAccelerationPerTick, first.DeltaNativeZ, 10);
        Assert.Equal(GcAranosOpeningLiftSession.RiseAccelerationPerTick, lift.RiseStepPerTick, 10);

        for (int i = 0; i < 100; i++)
        {
            lift.AdvanceNativeTick();
        }

        Assert.Equal(GcAranosOpeningLiftSession.MaxRiseStepPerTick, lift.RiseStepPerTick, 10);
    }

    [Fact]
    public void OpeningTripReachesRetailUpperTargetAndStops()
    {
        var lift = new GcAranosOpeningLiftSession(Source());
        lift.TryBeginRise(riderPresent: true);

        int ticks = 0;
        while (lift.Phase != GcAranosOpeningLiftPhase.UpperIdle && ticks++ < 1000)
        {
            lift.AdvanceNativeTick();
        }

        Assert.InRange(ticks, 500, 600);
        Assert.Equal(GcAranosOpeningLiftPhase.UpperIdle, lift.Phase);
        Assert.Equal(GcAranosOpeningLiftSession.UpperNativeZ, lift.CurrentNativeZ, 10);
        Assert.Equal(0d, lift.AdvanceNativeTick().DeltaNativeZ);
    }

    [Fact]
    public void RejectsOtherNativeObjects()
    {
        var wrong = Source() with { InstanceIndex = 165 };
        Assert.Throws<InvalidDataException>(() => new GcAranosOpeningLiftSession(wrong));
    }

    private static RuntimeDynamicObject Source()
    {
        var matrix = new double[16];
        matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1d;
        matrix[12] = 246.985;
        matrix[13] = 50.031;
        matrix[14] = 202.939;
        return new RuntimeDynamicObject(
            "rac2",
            GcAranosOpeningLiftSession.NativeClassId,
            GcAranosOpeningLiftSession.OpeningInstanceIndex,
            13,
            "moby-class-2753",
            "gc:0:164",
            new RuntimeObjectTransform(matrix),
            Array.Empty<RuntimeObjectMesh>());
    }
}
