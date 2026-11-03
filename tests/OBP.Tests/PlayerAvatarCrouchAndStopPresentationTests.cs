using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using Xunit;
using static OBP.Tests.PlayerAvatarTestFixtures;

namespace OBP.Tests;

/// <summary>
/// Crouch entry/exit/turn blends and locomotion stop-variant selection.
/// </summary>
public sealed class PlayerAvatarCrouchAndStopPresentationTests
{
    [Fact]
    public void CrouchEntersAtFrameTwoAndShortTapWaitsForReleaseAgeThenUsesCurve()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 0, new(false, true, 0, true));
        Assert.Equal(new Rac1AnimationFrameClock.Sample(13, 2, 2, 0, true), controller.CrouchSample);
        for (int tick = 1; tick <= 11; tick++)
        {
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d, new(false, true, 0));
            Assert.Equal("13", controller.Current.SourceSequenceKey);
            Assert.Equal("4", controller.Current.SourceStateKey);
            if (tick == 9)
                Assert.Equal(new Rac1AnimationFrameClock.Sample(13, 2, 3, 0, false), controller.CrouchSample);
        }
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 12d / 60d, new(false, true, 0));
        Assert.Equal("0", controller.Current.SourceSequenceKey);
        double source = controller.Current.SampledPose!.LocalPositions[0];
        for (int tick = 1; tick <= 15; tick++)
        {
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, (12d + tick) / 60d, new(false, true, 0));
            Assert.True(controller.CrouchSample!.Value.IsBlending);
            Assert.Equal(Rac1CrouchAnimation.StandUpBlend[tick - 1], controller.CrouchSample.Value.Fraction);
            Assert.Equal(source * (1d - Rac1CrouchAnimation.StandUpBlend[tick - 1]),
                controller.Current.SampledPose!.LocalPositions[0], 6);
        }
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 28d / 60d, new(false, true, 0));
        Assert.Null(controller.CrouchSample);
        Assert.NotNull(controller.OrdinaryStandingSample);
        Assert.NotNull(controller.Current.SampledPose);
        Assert.Equal("0", controller.Current.SourceSequenceKey);
    }

    [Fact]
    public void CrouchTurnUsesAngularVelocityAndSnapshotsAnInterruptedBlend()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 0, new(false, true, 0, true));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 1d / 60d, new(false, true, 0, true));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 2d / 60d, new(false, true, 0, true, 1, 0.1f));
        Assert.Equal("14", controller.Current.SourceSequenceKey);
        Assert.Equal(4d / 9d, controller.Current.SampledPose!.LocalPositions[0], 6);
        Assert.Equal(new Rac1AnimationFrameClock.Sample(14, 0, 0, 0, true), controller.CrouchSample);
        float multiplier = controller.CrouchPlaybackMultiplier!.Value;
        for (int tick = 3; tick <= 7; tick++)
        {
            // Neither the clip nor its previous rate is overwritten in the
            // small, nonzero angular-velocity interval (even with no stick).
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d,
                new(false, true, 0, true, 0, Rac1CrouchAnimation.TurnVelocityThreshold));
            Assert.Equal("14", controller.Current.SourceSequenceKey);
            Assert.Equal(multiplier, controller.CrouchPlaybackMultiplier);
        }
        Assert.Equal(new Rac1AnimationFrameClock.Sample(14, 0, 1, 0, false), controller.CrouchSample);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 8d / 60d, new(false, true, 0, true));
        Assert.Equal(new Rac1AnimationFrameClock.Sample(13, 0, 0, 0, true), controller.CrouchSample);
        for (int tick = 9; tick <= 15; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d, new(false, true, 0, true));
        Assert.Equal(new Rac1AnimationFrameClock.Sample(13, 0, 1, 0, false), controller.CrouchSample);
    }

    [Fact]
    public void MovingCrouchEntryUsesLongerBlendAndActionsInterruptWithoutStalePose()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, 0, new(true, true, 0));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 1d / 60d, new(false, true, 0, true));
        // Walking advances first, then the entry snapshots its 1/8 blend to frame 8.
        Assert.Equal(1d, controller.Current.SampledPose!.LocalPositions[0], 6);
        Assert.Null(controller.OrdinaryLocomotionSample);
        for (int tick = 2; tick <= 14; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d, new(false, true, 0, true));
        Assert.True(controller.CrouchSample!.Value.IsBlending);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 15d / 60d, new(false, true, 0, true));
        Assert.False(controller.CrouchSample!.Value.IsBlending);
        controller.StepOrdinaryAnimation(PlayerAnimationState.JumpRise, 16d / 60d, default);
        Assert.Null(controller.CrouchSample);
        Assert.Null(controller.Current.SampledPose);
        Assert.Equal("7", controller.Current.SourceSequenceKey);
        controller.ResetPhysicsPresentation();
        Assert.Null(controller.CrouchSample);
        Assert.Equal("0", controller.Current.SourceSequenceKey);
    }

    [Fact]
    public void CrouchExitCanBeInterruptedByWalkingOrAnotherCrouch()
    {
        foreach (bool repress in new[] { false, true })
        {
            var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 0, new(false, true, 0, true));
            for (int tick = 1; tick <= 12; tick++)
                controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d, new(false, true, 0));
            double source = controller.Current.SampledPose!.LocalPositions[0];
            controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, 13d / 60d,
                new(!repress, true, 0, repress));
            Assert.Equal(repress ? "13" : "3", controller.Current.SourceSequenceKey);
            Assert.Equal(source * (1d - 0.1f), controller.Current.SampledPose!.LocalPositions[0], 6);
            controller.ResetPhysicsPresentation();
            Assert.Null(controller.CrouchSample);
            Assert.Null(controller.OrdinaryLocomotionSample);
        }
    }

    [Fact]
    public void RunReleaseWaitsForDelayedInputThenSelectsStopWithNineUpdateBlend()
    {
        var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
        for (int tick = 0; tick < 40; tick++)
            StopStep(controller, tick, 1, .095f, .095f);
        StopStep(controller, 40, 0, .09f, .09f);
        Assert.Equal("4", controller.Current.SourceSequenceKey);
        StopStep(controller, 41, 0, .085f, .085f);
        Assert.Contains(controller.Current.SourceSequenceKey, new[] { "5", "6" });
        Assert.Equal("3", controller.Current.SourceStateKey);
        Assert.Equal(4, controller.OrdinaryStopSample!.Value.NextFrame);
        Assert.True(controller.OrdinaryStopSample.Value.IsBlending);
        for (int tick = 42; tick <= 49; tick++)
        {
            StopStep(controller, tick, 0, .05f, .05f);
            Assert.True(controller.OrdinaryStopSample!.Value.IsBlending);
        }
        StopStep(controller, 50, 0, .05f, .05f);
        Assert.False(controller.OrdinaryStopSample!.Value.IsBlending);
    }

    [Fact]
    public void GradualReleaseUsesThirdStopAndFullRestartSnapshotsInterruptedPose()
    {
        var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
        for (int tick = 0; tick < 40; tick++)
            StopStep(controller, tick, tick < 35 ? 1 : .6f, .095f, .095f);
        StopStep(controller, 40, 0, .09f, .09f);
        StopStep(controller, 41, 0, .085f, .085f);
        Assert.Equal("20", controller.Current.SourceSequenceKey);
        Assert.Equal(7, controller.OrdinaryStopSample!.Value.NextFrame);
        StopStep(controller, 42, 1, .08f, .08f);
        Assert.Equal("20", controller.Current.SourceSequenceKey);
        StopStep(controller, 43, 1, .08f, .08f);
        Assert.Null(controller.OrdinaryStopSample);
        Assert.Equal(new Rac1AnimationFrameClock.Sample(3, 8, 8, 0, true), controller.OrdinaryLocomotionSample);
        var restarted = controller.Current;
        Assert.NotNull(restarted.SampledPose);
        controller.SetClock(1000);
        Assert.Same(restarted, controller.Current);
        controller.ResetPhysicsPresentation();
        Assert.Null(controller.OrdinaryStopSample);
        Assert.Null(controller.OrdinaryLocomotionSample);
        Assert.Equal("0", controller.Current.SourceSequenceKey);
    }

    [Fact]
    public void WalkReleaseReturnsToStandingCurveAndStopCompletionAlsoSettles()
    {
        foreach (float speed in new[] { .015f, .095f })
        {
            var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
            for (int tick = 0; tick < 40; tick++)
                StopStep(controller, tick, speed < .02f ? .3f : 1, speed, speed);
            for (int tick = 40; tick < 120; tick++)
                StopStep(controller, tick, 0, 0, tick < 42 ? speed : 0);
            Assert.Equal("0", controller.Current.SourceSequenceKey);
            Assert.Equal("0", controller.Current.SourceStateKey);
            Assert.False(controller.OrdinaryStopSample!.Value.IsBlending);
            Assert.NotNull(controller.Current.SampledPose);
        }
    }

    [Fact]
    public void ZeroScalarRestartChangesActionBeforeSelectingLocomotionOnTheNextUpdate()
    {
        var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
        for (int tick = 0; tick < 40; tick++)
            StopStep(controller, tick, 1, .095f, .095f);
        StopStep(controller, 40, 0, .09f, .09f);
        StopStep(controller, 41, 0, .085f, .085f);
        string? stop = controller.Current.SourceSequenceKey;
        StopStep(controller, 42, .3f, 0, 0);
        StopStep(controller, 43, .3f, 0, 0);
        Assert.Equal(stop, controller.Current.SourceSequenceKey);
        Assert.Equal("0", controller.Current.SourceStateKey);
        Assert.NotNull(controller.OrdinaryStopSample);
        StopStep(controller, 44, .3f, 0, 0);
        Assert.Equal("3", controller.Current.SourceSequenceKey);
        Assert.Equal("2", controller.Current.SourceStateKey);
        Assert.Null(controller.OrdinaryStopSample);
    }

    [Theory]
    [InlineData(PlayerAnimationState.JumpRise, "8")]
    [InlineData(PlayerAnimationState.Attack, "23")]
    public void StopCannotSurviveAnUnrelatedAction(PlayerAnimationState state, string expectedSequence)
    {
        var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
        for (int tick = 0; tick < 40; tick++)
            StopStep(controller, tick, 1, .095f, .095f);
        StopStep(controller, 40, 0, .09f, .09f);
        StopStep(controller, 41, 0, .085f, .085f);
        controller.StepOrdinaryAnimation(state, 42d / 60, default);
        Assert.Null(controller.OrdinaryStopSample);
        Assert.Null(controller.Current.SampledPose);
        Assert.Equal(expectedSequence, controller.Current.SourceSequenceKey);
    }
}
