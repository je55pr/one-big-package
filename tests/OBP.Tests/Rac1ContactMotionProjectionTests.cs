using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1ContactMotionProjectionTests
{
    [Fact]
    public void ObliqueContactReducesLengthButKeepsRequestDirection()
    {
        // A 3-4-5 request, with actual contact motion along X: the dot length
        // is 3, and the native set-length source is still the 3-4-5 request.
        var motion = Rac1ContactMotionProjection.Resolve(new(3, 4, 0), new(0.1, 0, 0));
        Assert.Equal(1.8, motion.X, 12);
        Assert.Equal(2.4, motion.Y, 12);
        Assert.Equal(0, motion.Z);
        Assert.Equal(3, motion.Length, 12);
    }

    [Fact]
    public void GroundDownwardRequestAffectsCarryEvenWhenFloorCancelsVerticalTravel()
    {
        var motion = Rac1ContactMotionProjection.Resolve(new(0.09, 0, -0.015), new(0.09, 0, 0));
        Assert.Equal(0.09, motion.Length, 12);
        Assert.True(motion.X < 0.09);
        Assert.True(motion.Z < 0);
        Assert.Equal(-1d / 6d, motion.Z / motion.X, 12);
        // Magnitude of actual displacement is not used as a distance clamp.
        Assert.Equal(motion,
            Rac1ContactMotionProjection.Resolve(new(0.09, 0, -0.015), new(0.009, 0, 0)));
    }

    [Fact]
    public void ZeroOpposedAndPerpendicularDisplacementDoNotCreateCarry()
    {
        foreach (var displacement in new Rac1NativeVector3[] { new(0, 0, 0), new(-1, 0, 0), new(0, 1, 0) })
            Assert.Equal(Rac1NativeVector3.Zero,
                Rac1ContactMotionProjection.Resolve(new(0.1, 0, 0), displacement));
        Assert.Equal(Rac1NativeVector3.Zero,
            Rac1ContactMotionProjection.Resolve(new(0, 0, 0), new(1, 0, 0)));
    }

    [Fact]
    public void EntryCapsFullThreeDimensionalCarryBeforePlanarDecay()
    {
        var carry = Rac1ContactMotionProjection.EnterCrouch(new(3, 0, 4));
        Assert.Equal(7d / 60d, carry.Length, 12);
        Assert.Equal(0.07, carry.X, 12);
        Assert.Equal(7d / 75d, carry.Z, 12);
    }

    [Fact]
    public void ContactFeedbackSeedsOnlyTheNextCrouchEntry()
    {
        var controller = RunningController();
        var ground = new PlayerContactFacts(true);
        var crouch = new PlayerControlIntent(0, 1, false, false, true);
        controller.RecordOrdinaryContactMotion(new(0.09, 0, -0.015), new(0.09, 0, 0));
        double contactPlanar = controller.ContactProcessedMotion!.Value.X;
        var first = controller.Step(crouch, ground, _ => 0);
        Assert.Equal(contactPlanar - Rac1RatchetMovementController.CrouchDecelerationPerTick,
            first.PlanarX, 12);
        Assert.Equal(0, first.PlanarY, 12);

        controller.RecordOrdinaryContactMotion(new(0.09, 0, -0.015), new(0, 0, 0));
        var second = controller.Step(crouch, ground, _ => 0);
        Assert.Equal(first.PlanarX - Rac1RatchetMovementController.CrouchDecelerationPerTick,
            second.PlanarX, 12);
        controller.Step(new(0, 1, false, false, false), ground, _ => 0);
        var reentry = controller.Step(crouch, ground, _ => 0);
        Assert.Equal(0, reentry.PlanarMagnitude);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionCancellationAndResetClearContactHistory(bool reset)
    {
        var controller = RunningController();
        controller.RecordOrdinaryContactMotion(new(0.09, 0, 0), new(0.09, 0, 0));
        if (reset) controller.Reset();
        else controller.CancelOrdinaryPlanarMotionForAction();
        Assert.Null(controller.ContactProcessedMotion);
        Assert.Equal(0, controller.Step(new(0, 1, false, false, true),
            new PlayerContactFacts(true), _ => 0).PlanarMagnitude);
    }

    [Fact]
    public void InvalidSamplesCannotReplaceTheLastValidMotion()
    {
        var controller = RunningController();
        controller.RecordOrdinaryContactMotion(new(0.09, 0, 0), new(0.09, 0, 0));
        var previous = controller.ContactProcessedMotion;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.RecordOrdinaryContactMotion(new(double.NaN, 0, 0), new(0.09, 0, 0)));
        Assert.Equal(previous, controller.ContactProcessedMotion);
        controller.ClearOrdinaryContactMotion();
        Assert.Null(controller.ContactProcessedMotion);
    }

    private static Rac1RatchetMovementController RunningController()
    {
        var controller = new Rac1RatchetMovementController();
        for (int tick = 0; tick < 60; tick++)
            controller.Step(new(0, 1, false, false, false), new PlayerContactFacts(true), _ => 0);
        return controller;
    }
}
