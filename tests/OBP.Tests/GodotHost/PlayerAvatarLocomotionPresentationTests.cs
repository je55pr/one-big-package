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
/// Walk/run sequencing, jumps, attacks, physics-rate locomotion blending and non-looping traversal.
/// </summary>
public sealed class PlayerAvatarLocomotionPresentationTests
{
    [Fact]
    public void SteadyWalkKeepsSequence3UntilRecoveredRunBoundarySignal()
    {
        var playback = NewPlayback();

        playback.SetClock(1.0);
        playback.SetAnimationState(PlayerAnimationState.Walk);
        Assert.Equal(PlayerAvatarAnimationRole.LocomotionStart, playback.CurrentClip.Role);
        Assert.Equal(1.0, playback.ClipStartedAtSeconds, 12);

        Assert.True(playback.CurrentAnimationPresentation.Loop);
        playback.SetClock(1.15);
        Assert.Equal(1, playback.CurrentFrame);
        playback.SetClock(1.21);
        Assert.Equal(0, playback.CurrentFrame);

        playback.SetClock(2.0);
        Assert.Equal(PlayerAnimationState.Walk, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.LocomotionStart, playback.CurrentClip.Role);

        playback.SetAnimationState(PlayerAnimationState.Run);
        Assert.Equal(PlayerAnimationState.Run, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
        Assert.Equal(2.0, playback.ClipStartedAtSeconds, 12);
    }

    [Fact]
    public void RunBoundaryPromotesSequence3To4WithoutClockCompletion()
    {
        var playback = NewPlayback();

        playback.SetClock(2.0);
        playback.SetAnimationState(PlayerAnimationState.Walk);
        playback.SetClock(2.05);
        playback.SetAnimationState(PlayerAnimationState.Run);

        Assert.Equal(PlayerAnimationState.Run, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
        Assert.Equal(2.05, playback.ClipStartedAtSeconds, 12);

        playback.SetClock(3.0);
        playback.SetAnimationState(PlayerAnimationState.Walk);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
        Assert.Equal(2.05, playback.ClipStartedAtSeconds, 12);
    }

    [Fact]
    public void JumpAndAttackInterruptLocomotionStartWithoutResumingIt()
    {
        var jumping = NewPlayback();
        jumping.SetAnimationState(PlayerAnimationState.Run);
        Assert.Equal(PlayerAvatarAnimationRole.LocomotionStart, jumping.CurrentClip.Role);
        jumping.SetClock(0.05);
        jumping.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal(PlayerAvatarAnimationRole.MovingJump, jumping.CurrentClip.Role);

        var attacking = NewPlayback();
        attacking.SetAnimationState(PlayerAnimationState.Walk);
        attacking.SetClock(0.05);
        attacking.SetAnimationState(PlayerAnimationState.Attack);
        Assert.Equal(PlayerAvatarAnimationRole.PrimaryAttack, attacking.CurrentClip.Role);
        attacking.SetAnimationState(PlayerAnimationState.Walk);
        attacking.SetClock(0.36);
        Assert.Equal(PlayerAnimationState.Walk, attacking.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, attacking.CurrentClip.Role);
    }

    [Fact]
    public void MovingToIdleReturnsDirectlyToStandingWithoutSelectingStopVariant()
    {
        var playback = NewPlayback();
        playback.SetAnimationState(PlayerAnimationState.Walk);
        playback.SetAnimationState(PlayerAnimationState.Run);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);

        playback.SetAnimationState(PlayerAnimationState.Idle);

        Assert.Equal(PlayerAnimationState.Idle, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.Standing, playback.CurrentClip.Role);
        Assert.NotEqual(PlayerAvatarAnimationRole.LocomotionStopVariant, playback.CurrentClip.Role);
    }

    [Fact]
    public void JumpRiseChoosesStationaryOrMovingClipFromLaunchContext()
    {
        var stationary = NewPlayback();
        stationary.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal(PlayerAvatarAnimationRole.StationaryJump, stationary.CurrentClip.Role);

        var moving = NewPlayback();
        moving.SetAnimationState(PlayerAnimationState.Run);
        moving.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal(PlayerAvatarAnimationRole.MovingJump, moving.CurrentClip.Role);
    }

    [Fact]
    public void JumpRiseToFallKeepsSelectedAirborneClipAndClockOrigin()
    {
        var playback = NewPlayback();
        playback.SetAnimationState(PlayerAnimationState.Run);
        playback.SetClock(0.5);
        playback.SetAnimationState(PlayerAnimationState.JumpRise);
        string clip = playback.CurrentClip.Id;
        double started = playback.ClipStartedAtSeconds;

        playback.SetClock(0.62);
        int before = playback.CurrentFrame;
        playback.SetAnimationState(PlayerAnimationState.Fall);

        Assert.Equal(PlayerAnimationState.Fall, playback.CurrentAnimationState);
        Assert.Equal(clip, playback.CurrentClip.Id);
        Assert.Equal(started, playback.ClipStartedAtSeconds);
        Assert.Equal(before, playback.CurrentFrame);
    }

    [Fact]
    public void LandReturnsDirectlyToStandingOrLaunchLocomotionClip()
    {
        var stationary = NewPlayback();
        stationary.SetAnimationState(PlayerAnimationState.JumpRise);
        stationary.SetAnimationState(PlayerAnimationState.Fall);
        stationary.SetAnimationState(PlayerAnimationState.Land);
        Assert.Equal(PlayerAvatarAnimationRole.Standing, stationary.CurrentClip.Role);

        var moving = NewPlayback();
        moving.SetAnimationState(PlayerAnimationState.Walk);
        moving.SetAnimationState(PlayerAnimationState.JumpRise);
        moving.SetAnimationState(PlayerAnimationState.Fall);
        moving.SetAnimationState(PlayerAnimationState.Land);
        Assert.Equal(PlayerAnimationState.Land, moving.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, moving.CurrentClip.Role);
    }

    [Fact]
    public void AttackPlaysOnceThenReturnsToLocomotionWithoutDroppingClockOvershoot()
    {
        var playback = NewPlayback();
        playback.SetAnimationState(PlayerAnimationState.Run);
        playback.SetClock(0.2);
        playback.SetAnimationState(PlayerAnimationState.Attack);

        Assert.Equal(PlayerAvatarAnimationRole.PrimaryAttack, playback.CurrentClip.Role);
        playback.SetClock(0.45);
        Assert.Equal(PlayerAnimationState.Attack, playback.CurrentAnimationState);
        Assert.Equal(1, playback.CurrentFrame);

        playback.SetClock(0.55);
        Assert.Equal(PlayerAnimationState.Run, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
        Assert.Equal(0.5, playback.ClipStartedAtSeconds, 12);
        Assert.Equal(0.05, playback.ClipElapsedSeconds, 12);
    }

    [Fact]
    public void AttackPulseRemainsLatchedWhileMovementUpdatesReturnState()
    {
        var playback = NewPlayback();
        playback.SetAnimationState(PlayerAnimationState.Run);
        playback.SetClock(0.2);
        playback.SetAnimationState(PlayerAnimationState.Attack);

        // Wave2D emits Attack for one controller tick, then resumes Run.
        playback.SetAnimationState(PlayerAnimationState.Run);
        playback.SetClock(0.45);
        Assert.Equal(PlayerAnimationState.Attack, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.PrimaryAttack, playback.CurrentClip.Role);

        playback.SetClock(0.55);
        Assert.Equal(PlayerAnimationState.Run, playback.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
    }

    [Fact]
    public void GenericFallbackKeepsSourceKeysUnset()
    {
        var controller = new GenericPlayerAnimationPresentationController(Avatar());

        var presentation = controller.SetAnimationState(PlayerAnimationState.Walk);

        Assert.Equal("locomotion-start", presentation.ClipId);
        Assert.Null(presentation.SourceSequenceKey);
        Assert.Null(presentation.SourceStateKey);
    }

    [Fact]
    public void Rac1ControllerCarriesNativeSequenceAndStateKeysWithoutGodotTypes()
    {
        var playback = NewPlayback();

        playback.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal("7", playback.CurrentAnimationPresentation.SourceSequenceKey);
        Assert.Equal("7", playback.CurrentAnimationPresentation.SourceStateKey);

        playback.SetAnimationState(PlayerAnimationState.Land);
        playback.SetAnimationState(PlayerAnimationState.Attack);
        Assert.Equal("23", playback.CurrentAnimationPresentation.SourceSequenceKey);
        Assert.Equal("19", playback.CurrentAnimationPresentation.SourceStateKey);
    }

    [Fact]
    public void PhysicsLocomotionIgnoresSemanticRunThresholdAndRenderClock()
    {
        var avatar = LocomotionAvatar();
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        var playback = new PlayerAvatarView.Playback(avatar, controller.Current);
        var movement = new Rac1RatchetMovementController();
        for (int update = 1; update <= 20; update++)
        {
            movement.Step(new PlayerControlIntent(0, 1, false, false, false), new PlayerContactFacts(true), _ => 0d);
            // Deliberately wrong semantic Run from the first update: only the
            // updated controller scalar should determine the native 3/4 transition.
            var presentation = controller.StepOrdinaryAnimation(PlayerAnimationState.Run, update / 60d,
                new(true, true, (float)movement.PlanarMagnitude));
            Assert.Equal(update < 19 ? "3" : "4", presentation.SourceSequenceKey);
            Assert.Equal("2", presentation.SourceStateKey);
            playback.SetAnimationPresentation(presentation);
            int frame = playback.CurrentFrame;
            for (int render = 0; render < 7; render++)
            {
                playback.SetClock(100 + render);
                Assert.Equal(frame, playback.CurrentFrame);
                Assert.Same(presentation, controller.SetClock(100 + render));
            }
        }
    }

    [Fact]
    public void PhysicsBlendSamplesContinuouslyAndSnapshotsInterruptedBlend()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, 0, new(true, true, 0));
        Assert.Equal(0d, controller.Current.SampledPose!.LocalPositions[0]);
        for (int update = 1; update <= 5; update++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, update / 60d, new(true, true, 0.095f));
        // At update five the standing->walk blend has reached 5/8 of frame 8.
        // Run selection must snapshot that pose, not restart from a clip frame.
        Assert.Equal("4", controller.Current.SourceSequenceKey);
        Assert.Equal(5d, controller.Current.SampledPose!.LocalPositions[0], 8);
        Assert.Equal(0f, controller.OrdinaryLocomotionSample!.Value.Fraction);
        int target = controller.OrdinaryLocomotionSample.Value.NextFrame;
        controller.StepOrdinaryAnimation(PlayerAnimationState.Run, 6d / 60d, new(true, true, 0.095f));
        Assert.Equal(5d + (target - 5d) / 8d, controller.Current.SampledPose!.LocalPositions[0], 8);
    }

    [Theory]
    [InlineData(PlayerAnimationState.JumpRise, "8")]
    [InlineData(PlayerAnimationState.Attack, "23")]
    [InlineData(PlayerAnimationState.Idle, "0")]
    public void PhysicsLocomotionHandsOtherActionsBackAndResetClearsItsHistory(PlayerAnimationState state, string sequence)
    {
        var controller = new Rac1PlayerAnimationPresentationController(LocomotionAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, 0, new(true, true, 0));
        controller.StepOrdinaryAnimation(state, 1d / 60d, default);
        Assert.Null(controller.Current.SampledPose);
        Assert.Null(controller.OrdinaryLocomotionSample);
        Assert.Equal(sequence, controller.Current.SourceSequenceKey);
        controller.ResetPhysicsPresentation();
        Assert.Equal("0", controller.Current.SourceSequenceKey);
        Assert.Equal(0, controller.Current.ClipStartedAtSeconds);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Run, 0, new(true, true, 0.095f));
        Assert.Equal(new Rac1AnimationFrameClock.Sample(3, 8, 8, 0, true), controller.OrdinaryLocomotionSample);
    }

    [Theory]
    [InlineData(.095f, true, 4, 1.33f)]
    [InlineData(.015f, true, 3, 2.355f)]
    [InlineData(.095f, false, 4, 1.33f)]
    public void JumpReturnReattachesSpeedScaledPhysicsLocomotionAndCanRepeat(
        float scalar, bool hasLandState, int expectedSequence, float expectedMultiplier)
    {
        var controller = new Rac1PlayerAnimationPresentationController(StopAvatar());
        var playback = new PlayerAvatarView.Playback(StopAvatar(), controller.Current);
        int tick = 0;
        for (; tick < 40; tick++)
            StopStep(controller, tick, 1, .095f, .095f);
        for (int jump = 0; jump < 2; jump++)
        {
            controller.StepOrdinaryAnimation(PlayerAnimationState.JumpRise, tick++ / 60d,
                new(false, false, scalar, InputMagnitude: 1));
            controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick++ / 60d,
                new(false, false, scalar, InputMagnitude: 1));
            controller.StepOrdinaryAnimation(hasLandState ? PlayerAnimationState.Land : PlayerAnimationState.Run,
                tick++ / 60d, new(true, true, scalar, InputMagnitude: 1, ContactMotionLength: scalar));
            Assert.NotNull(controller.OrdinaryLocomotionSample);
            Assert.NotNull(controller.Current.SampledPose);
            for (int update = 0; update < 60; update++)
            {
                StopStep(controller, tick++, 1, scalar, scalar);
                Assert.NotNull(controller.OrdinaryLocomotionSample);
                playback.SetAnimationPresentation(controller.Current);
                int frame = playback.CurrentFrame;
                var presentation = controller.Current;
                playback.SetClock(1000 + tick);
                Assert.Equal(frame, playback.CurrentFrame);
                Assert.Same(presentation, controller.SetClock(1000 + tick));
            }
            Assert.Equal(expectedSequence.ToString(), controller.Current.SourceSequenceKey);
            Assert.Equal(expectedMultiplier, controller.OrdinaryLocomotionMultiplier!.Value, 5);
        }
    }

    [Fact]
    public void NonLoopingTraversalPublishesCompletionOnceWhileHoldingItsTerminalPose()
    {
        var avatar = Avatar();
        var traversal = Clip("thruster-power-slam", PlayerAvatarAnimationRole.Traversal,
            [1d / 30, 1d / 30, 1d / 30]) with
        { FrameTimelinePositions = [0, 1, 2] };
        avatar = avatar with { AnimationClips = [.. avatar.AnimationClips, traversal] };
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        int completions = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick / 60d,
                new(false, false, 0, Traversal: new(0x22, 42, 1, BlendTicks: 1)));
            if (controller.TraversalAnimationCycleCompleted) completions++;
        }
        Assert.Equal(1, completions);
        Assert.Equal(2, controller.Current.SampledPose!.FrameIndex);
        controller.ResetPhysicsPresentation();
        Assert.False(controller.TraversalAnimationCycleCompleted);
    }
}
