using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1ExtendedMovementTests
{
    private static readonly Rac1PlayerContactResult Ground = Rac1PlayerContactResult.StaticWorld(true);
    private static readonly Rac1PlayerContactResult Air = Rac1PlayerContactResult.StaticWorld(false);
    private static readonly Rac1MovementContext NoPack = new(0, false, 0, 0, 0, 4);

    [Fact]
    public void OrdinaryGroundedControlRetainsExistingWitnessedSteps()
    {
        var original = new Rac1RatchetMovementController();
        var extended = new Rac1RatchetMovementController();
        foreach (var input in Enumerable.Repeat(new PlayerControlIntent(0, 1, false, false), 75)
            .Concat(Enumerable.Repeat(new PlayerControlIntent(0, 0, false, false), 60)))
        {
            var expected = original.Step(input, Ground, _ => 0);
            var actual = extended.StepExtended(input, Ground, NoPack, _ => 0);
            Assert.Equal(expected, actual);
            Assert.Null(extended.TraversalAction);
        }
    }

    [Fact]
    public void NoPackCrouchJumpUsesOrdinaryJumpRatherThanRemainingCrouched()
    {
        var controller = new Rac1RatchetMovementController();
        for (int tick = 0; tick < 7; tick++)
            controller.StepExtended(new(0, 0, false, false, true), Ground, NoPack);
        var step = controller.StepExtended(new(0, 0, true, true, true), Ground, NoPack);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, step.Phase);
        Assert.Null(controller.TraversalAction);
    }

    [Theory]
    [InlineData(2, 15, 21)]
    [InlineData(3, 13, 17)]
    public void PackHighJumpOwnsItsProfileAndReturnsControlOnLanding(int pack, int action, int sequence)
    {
        var context = NoPack with { ActivePackId = pack, ClankAvailable = true };
        var controller = new Rac1RatchetMovementController();
        for (int tick = 0; tick < 7; tick++)
            controller.StepExtended(new(0, 0, false, false, true), Ground, context);
        controller.StepExtended(new(0, 0, true, true, true), Ground, context);
        Assert.Equal(action, controller.TraversalAction);
        Assert.Equal(sequence, controller.TraversalAnimation!.Value.Sequence);
        bool launched = false;
        for (int tick = 0; tick < 16; tick++)
        {
            var step = controller.StepExtended(new(0, 0, true, false), launched ? Air : Ground, context);
            Assert.True(double.IsFinite(step.Vertical));
            launched |= step.Vertical > 0;
        }
        Assert.True(launched);
        for (int tick = 0; tick < 180 && controller.VerticalStep > 0; tick++)
            controller.StepExtended(new(0, 0, false, false), Air, context);
        for (int tick = 0; tick < 16 && controller.TraversalAction is not null; tick++)
            controller.StepExtended(new(0, 0, false, false), Ground, context);
        Assert.Null(controller.TraversalAction);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, controller.Phase);
    }

    [Fact]
    public void SideChordSelectsNativeFlipAndHeldCrossDoesNotRestartItsEntry()
    {
        var controller = new Rac1RatchetMovementController();
        controller.StepExtended(new(1, 0, true, true, true), Ground, NoPack);
        Assert.Equal(11, controller.TraversalAction);
        long generation = controller.TraversalAnimation!.Value.EntryGeneration;
        Assert.Equal(29, controller.TraversalAnimation.Value.Sequence);
        for (int tick = 0; tick < 12; tick++)
            controller.StepExtended(new(1, 0, true, false, true), tick < 6 ? Ground : Air, NoPack);
        Assert.Equal(generation, controller.TraversalAnimation!.Value.EntryGeneration);
        Assert.True(controller.VerticalStep > 0);
    }

    [Theory]
    [InlineData(2, -.036f)]
    [InlineData(3, -.06f)]
    public void SecondCrossBoostCanReachGlideThenReleaseResumesFalling(int pack, float descent)
    {
        var controller = new Rac1RatchetMovementController();
        var context = NoPack with { ActivePackId = pack, ClankAvailable = true, GroundClearance = 2 };
        controller.StepExtended(new(0, 0, true, true), Ground, context);
        for (int tick = 0; tick < 16; tick++)
            controller.StepExtended(new(0, 0, tick < 9, false), tick < 7 ? Ground : Air, context);
        controller.StepExtended(new(0, 0, true, true), Air, context);
        Assert.Equal(14, controller.TraversalAction);
        for (int tick = 0; tick < 45 && controller.TraversalAction != 8; tick++)
            controller.StepExtended(new(0, 0, true, false), Air, context);
        Assert.Equal(8, controller.TraversalAction);
        Assert.Equal(descent, controller.VerticalStep, 6);
        for (int tick = 0; tick < 30; tick++)
            controller.StepExtended(new(0, 0, false, false), Air, context);
        var falling = controller.StepExtended(new(0, 0, false, false), Air, context);
        Assert.Null(controller.TraversalAction);
        Assert.True(falling.Vertical < descent);
        Assert.Equal(Rac1RatchetMovementPhase.Falling, falling.Phase);
    }

    [Fact]
    public void ResetAndActionInterruptionClearSpecialMovement()
    {
        var controller = new Rac1RatchetMovementController();
        controller.StepExtended(new(1, 0, true, true, true), Ground, NoPack);
        controller.CancelOrdinaryPlanarMotionForAction();
        Assert.Null(controller.TraversalAction);
        controller.Reset();
        Assert.Null(controller.TraversalAction);
        Assert.Equal(0, controller.TraversalAge);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, controller.Phase);
    }

    [Fact]
    public void FirstCrossHoldCannotAutomaticallyBecomeGlideOrASecondJump()
    {
        var controller = new Rac1RatchetMovementController();
        var pack = NoPack with { ActivePackId = 2, ClankAvailable = true, GroundClearance = 2 };
        controller.StepExtended(new(0, 0, true, true), Ground, pack);
        for (int tick = 0; tick < 60; tick++)
        {
            controller.StepExtended(new(0, 0, true, false), tick < 7 ? Ground : Air, pack);
            Assert.Null(controller.TraversalAction);
        }
    }

    [Fact]
    public void BufferedSecondPressTriggersOneNativeAerialBoostWithoutEquipment()
    {
        var controller = new Rac1RatchetMovementController();
        controller.StepExtended(new(0, 0, true, true), Ground, NoPack);
        for (int tick = 0; tick < 10; tick++)
            controller.StepExtended(new(0, 0, tick < 7, false), tick < 7 ? Ground : Air, NoPack);
        controller.StepExtended(new(0, 0, true, true), Air, NoPack);
        for (int tick = 0; tick < 8 && controller.TraversalAction is null; tick++)
            controller.StepExtended(new(0, 0, true, false), Air, NoPack);
        Assert.Equal(14, controller.TraversalAction);
        long generation = controller.TraversalAnimation!.Value.EntryGeneration;
        controller.StepExtended(new(0, 0, false, false), Air, NoPack);
        controller.StepExtended(new(0, 0, true, true), Air, NoPack);
        Assert.Equal(generation, controller.TraversalAnimation!.Value.EntryGeneration);
    }

    [Fact]
    public void ThrusterPowerSlamRequiresFreshAirCrouchAndAnimationExitRestoresMovingControl()
    {
        var controller = new Rac1RatchetMovementController();
        var pack = NoPack with { ActivePackId = 3, ClankAvailable = true, GroundClearance = 2 };
        controller.StepExtended(new(0, 0, true, true), Ground, pack);
        for (int tick = 0; tick < 18; tick++)
            controller.StepExtended(new(0, 0, tick < 8, false), tick < 7 ? Ground : Air, pack);
        controller.StepExtended(new(0, 0, false, false, true), Air, pack);
        Assert.Equal(0x22, controller.TraversalAction);
        Assert.Equal(42, controller.TraversalAnimation!.Value.Sequence);
        Assert.Equal(11, controller.CurrentMovementDomain);
        Assert.False(Rac1LedgeProbe.AdmitsOrdinaryRatchetCandidate(controller.CurrentNativeMovementAction,
            controller.CurrentMovementDomain, false, -1, 100));
        for (int tick = 0; tick < 45; tick++)
            controller.StepExtended(new(0, 0, false, false), Air, pack);
        Assert.True(controller.VerticalStep < 0);
        controller.StepExtended(new(0, 0, false, false), Ground, pack);
        Assert.Equal(0x22, controller.TraversalAction);
        controller.StepExtended(new(0, 1, false, false), Ground,
            pack with { NativeAnimationExitRequested = true }, _ => 0);
        Assert.Null(controller.TraversalAction);
        Assert.True(controller.PlanarMagnitude > 0);
    }

    [Fact]
    public void PositiveTraversalReleaseCannotResumeAnEarlierHeldJump()
    {
        var fresh = new Rac1RatchetMovementController();
        var afterJump = new Rac1RatchetMovementController();
        afterJump.StepExtended(new(0, 0, true, true), Ground, NoPack);
        for (int tick = 0; tick < 12; tick++)
            afterJump.StepExtended(new(0, 0, true, false), tick < 7 ? Ground : Air, NoPack);
        foreach (var controller in new[] { fresh, afterJump })
        {
            controller.ResumeAfterTraversal(.02, .03, .15);
            var step = controller.StepExtended(new(0, 0, true, false), Air, NoPack);
            Assert.Equal(.15 - Rac1RatchetMovementController.ReleasedRiseGravityPerTick, step.Vertical, 12);
            Assert.Null(controller.TraversalAction);
        }
        Assert.Equal(fresh.VerticalStep, afterJump.VerticalStep);
    }

    [Fact]
    public void AdmittedJumpSurvivesSupportLossBeforeItsLaunchAge()
    {
        var controller = new Rac1RatchetMovementController();
        controller.StepExtended(new(0, 1, true, true), Ground, NoPack, _ => 0);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, controller.Phase);
        for (int tick = 1; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
            controller.StepExtended(new(0, 1, true, false), Air, NoPack, _ => 0);
        Assert.Equal(Rac1RatchetMovementPhase.Rising, controller.Phase);
        Assert.True(controller.VerticalStep > 0);
    }

    [Fact]
    public void HeliStretchUsesItsAuthoredRequestRampSeparatelyFromTheScalar()
    {
        var controller = new Rac1RatchetMovementController();
        var context = NoPack with { ActivePackId = 2, ClankAvailable = true };
        for (int tick = 0; tick < 48; tick++)
            controller.StepExtended(new(0, 1, false, false), Ground, context, _ => 0);
        for (int tick = 0; tick < 8; tick++)
            controller.StepExtended(new(0, 1, false, false, true), Ground, context, _ => 0);
        controller.StepExtended(new(0, 1, true, true, true), Ground, context, _ => 0);
        Assert.Equal(0xa, controller.TraversalAction);
        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 1; tick <= 4; tick++)
            step = controller.StepExtended(new(0, 1, true, false), Ground, context, _ => 0);
        Assert.True(step.PlanarMagnitude > .12);
        Assert.True(controller.PlanarMagnitude < .1);
    }

    [Fact]
    public void NeutralFlipLandingRetainsTheAirActionThenHandsOffToIdleWithBrakedContactCarry()
    {
        var controller = new Rac1RatchetMovementController();
        controller.StepExtended(new(1, 0, true, true, true), Ground, NoPack);
        for (int tick = 0; tick < 60 && (tick < 8 || controller.VerticalStep > 0); tick++)
            controller.StepExtended(new(0, 0, false, false), tick < 5 ? Ground : Air, NoPack);
        Assert.Equal(0, controller.PlanarMagnitude);
        bool sawLanding = false;
        for (int tick = 0; tick < 16 && controller.TraversalAction is not null; tick++)
        {
            var motion = new Rac1NativeVector3(controller.PlanarX, controller.PlanarY, controller.VerticalStep);
            controller.RecordOrdinaryContactMotion(motion, motion with { Z = 0 });
            controller.StepExtended(new(0, 0, false, false), Ground, NoPack);
            sawLanding |= controller.IsLanding;
            Assert.NotEqual(2, controller.GroundAnimationEntry?.NativeAction);
        }
        Assert.True(sawLanding);
        Assert.Equal(0, controller.GroundAnimationEntry?.NativeAction);
        Assert.Equal(0, controller.GroundAnimationEntry?.BlendTicks);
        Assert.Null(controller.TraversalAction);
        for (int tick = 0; tick < 20; tick++)
            controller.StepExtended(new(0, 0, false, false), Ground, NoPack);
        Assert.Equal(0, controller.PlanarMagnitude);
    }
}
