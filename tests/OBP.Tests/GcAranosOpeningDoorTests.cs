using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class GcAranosOpeningDoorTests
{
    [Fact]
    public void DoorStaysClosedAtRetailBoundaryAndOpensStrictlyInsideSix()
    {
        var door = new GcAranosOpeningDoorSession(Source());

        Assert.False(door.ObservePlayerDistance(GcAranosOpeningDoorSession.OpenTriggerDistance));
        Assert.Equal(GcAranosOpeningDoorPhase.Closed, door.Phase);
        Assert.Equal(0, door.NativeState);

        Assert.True(door.ObservePlayerDistance(
            GcAranosOpeningDoorSession.OpenTriggerDistance - 0.001));
        Assert.Equal(GcAranosOpeningDoorPhase.Opening, door.Phase);
        Assert.Equal(1, door.NativeState);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, door.EntityState.Presentation.AnimationRole);
    }

    [Fact]
    public void OpeningSequenceLatchesToNativeStateTwoAfterSixtyTicks()
    {
        var door = new GcAranosOpeningDoorSession(Source());
        Assert.True(door.ObservePlayerDistance(5.5));

        for (int tick = 0; tick < GcAranosOpeningDoorSession.OpeningNativeTicks - 1; tick++)
        {
            Assert.Equal(GcAranosOpeningDoorPhase.Opening, door.AdvanceNativeTick());
        }

        Assert.Equal(GcAranosOpeningDoorSession.OpeningNativeTicks - 1, door.OpeningTicks);
        Assert.Equal(GcAranosOpeningDoorPhase.Open, door.AdvanceNativeTick());
        Assert.Equal(GcAranosOpeningDoorSession.OpeningNativeTicks, door.OpeningTicks);
        Assert.Equal(2, door.NativeState);
        Assert.Equal(GcAranosOpeningDoorPhase.Open, door.AdvanceNativeTick());
    }

    [Fact]
    public void OpeningTriggerIsOneShot()
    {
        var door = new GcAranosOpeningDoorSession(Source());

        Assert.True(door.ObservePlayerDistance(1));
        Assert.False(door.ObservePlayerDistance(1));
        Assert.Equal(GcAranosOpeningDoorPhase.Opening, door.Phase);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.01)]
    public void InvalidPlayerDistanceIsRejected(double distance)
    {
        var door = new GcAranosOpeningDoorSession(Source());
        Assert.Throws<ArgumentOutOfRangeException>(() => door.ObservePlayerDistance(distance));
    }

    [Fact]
    public void RejectsOtherNativeObjects()
    {
        var wrong = Source() with { InstanceIndex = GcAranosOpeningDoorSession.OpeningInstanceIndex - 1 };
        Assert.Throws<InvalidDataException>(() => new GcAranosOpeningDoorSession(wrong));
    }

    [Fact]
    public void OneShotAnimationFrameSelectionClampsToFinalPose()
    {
        var frames = Enumerable.Range(0, 31)
            .Select(_ => Array.Empty<double>())
            .ToArray();
        var clip = new RuntimeObjectAnimationClip(
            "opening",
            RuntimeObjectAnimationRole.Reaction,
            [new RuntimeObjectAnimationSurface(0, frames)],
            Enumerable.Repeat(1d / 30d, frames.Length).ToArray());

        Assert.Equal(0, clip.FrameIndexAt(0));
        Assert.Equal(29, clip.FrameIndexAt(0.999));
        Assert.Equal(30, clip.FrameIndexAt(1.0));
        Assert.Equal(30, clip.FrameIndexAt(10.0));
    }

    private static RuntimeDynamicObject Source()
    {
        var matrix = new double[16];
        matrix[0] = matrix[5] = matrix[10] = matrix[15] = 1d;
        return new RuntimeDynamicObject(
            "rac2",
            GcAranosOpeningDoorSession.NativeClassId,
            GcAranosOpeningDoorSession.OpeningInstanceIndex,
            1,
            "moby-class-2755",
            "gc:0:166",
            new RuntimeObjectTransform(matrix),
            Array.Empty<RuntimeObjectMesh>());
    }
}
