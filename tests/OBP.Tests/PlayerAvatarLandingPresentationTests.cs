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
/// Native landing entry, air-clock handoff and supported-idle interpolation.
/// </summary>
public sealed class PlayerAvatarLandingPresentationTests
{
    [Fact]
    public void SupportedIdleInterpolatesTheNativeSlowClipAtPhysicsRate()
    {
        var avatar = LandingAvatar();
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        var playback = new PlayerAvatarView.Playback(avatar, controller.Current);
        for (int tick = 0; tick <= 16; tick++)
        {
            var presentation = controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d,
                new(false, true, 0, ContactMotionLength: 0));
            Assert.Equal("0", presentation.SourceSequenceKey);
            Assert.Equal(tick / 8d, presentation.SampledPose!.LocalPositions[0], 6);
            Assert.Equal(tick / 8, controller.OrdinaryStandingSample!.Value.CurrentFrame);
            playback.SetAnimationPresentation(presentation);
            int frame = playback.CurrentFrame;
            playback.SetClock(1000 + tick);
            Assert.Equal(frame, playback.CurrentFrame);
            Assert.Same(presentation, controller.SetClock(1000 + tick));
        }
        controller.ResetPhysicsPresentation();
        Assert.Null(controller.OrdinaryStandingSample);
        Assert.Null(controller.Current.SampledPose);
    }

    [Fact]
    public void ReleasedLowVelocityLandingCannotReplayRunFromItsLaunchHistory()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LandingAvatar());
        for (int tick = 0; tick < 40; tick++)
            StopStep(controller, tick, 1, .095f, .095f);
        Assert.Equal("4", controller.Current.SourceSequenceKey);
        controller.StepOrdinaryAnimation(PlayerAnimationState.JumpRise, 40d / 60, default);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, 44d / 60, default);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Land, 46d / 60,
            new(false, true, .001f, ContactMotionLength: .001f));
        Assert.Equal("0", controller.Current.SourceSequenceKey);
        Assert.Null(controller.OrdinaryLocomotionSample);
        Assert.True(controller.OrdinaryStandingSample!.Value.IsBlending);
        double source = controller.Current.SampledPose!.LocalPositions[0];
        for (int tick = 1; tick <= 24; tick++)
        {
            // Residual collision velocity can still give the host a Walk label.
            controller.StepOrdinaryAnimation(PlayerAnimationState.Walk, (46d + tick) / 60,
                new(false, true, .0001f, ContactMotionLength: .0001f));
            Assert.Equal("0", controller.Current.SourceSequenceKey);
            Assert.NotNull(controller.Current.SampledPose);
            if (tick < 16)
                Assert.Equal(source * (1 - Rac1CrouchAnimation.StandUpBlend[tick - 1]),
                    controller.Current.SampledPose!.LocalPositions[0], 6);
        }
        Assert.False(controller.OrdinaryStandingSample!.Value.IsBlending);
        Assert.Equal(1d, controller.Current.SampledPose!.LocalPositions[0], 6);
    }

    [Theory]
    [InlineData(0, 0, 0, -2)]
    [InlineData(2, 4, 0, 8)]
    [InlineData(2, 4, 0, 14)]
    [InlineData(3, 5, 0, -1)]
    [InlineData(3, 5, 0, -2)]
    [InlineData(4, 13, 2, -2)]
    public void NativeLandingEntrySnapshotsAirPoseAndTransfersToItsOrdinaryClock(
        int action, int sequence, int nextFrame, int blendTicks)
    {
        var controller = new Rac1PlayerAnimationPresentationController(LandingAvatar());
        for (int tick = 0; tick < 9; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick / 60d,
                new(false, false, .01f, Traversal: new(0xb, 28, 1, BlendTicks: 3, Loop: true)));
        double source = controller.Current.SampledPose!.LocalPositions[0] + .25d;
        controller.StepOrdinaryAnimation(PlayerAnimationState.Land, 9d / 60,
            new(action == 2, true, .095f, CrouchHeld: action == 4, InputMagnitude: action == 2 ? 1 : 0,
                ContactMotionLength: .01f,
                GroundEntry: new(action, sequence, 1, nextFrame, blendTicks)));
        Assert.Equal(sequence.ToString(), controller.Current.SourceSequenceKey);
        Assert.Equal(action.ToString(), controller.Current.SourceStateKey);
        Assert.Equal(source, controller.Current.SampledPose!.LocalPositions[0], 6);
        Assert.False(controller.TraversalAnimationCycleCompleted);
        Assert.Equal(0, controller.TraversalAuthoredAnimationTime);
        var entry = action switch
        {
            0 => controller.OrdinaryStandingSample,
            2 => controller.OrdinaryLocomotionSample,
            3 => controller.OrdinaryStopSample,
            _ => controller.CrouchSample,
        };
        Assert.Equal(new Rac1AnimationFrameClock.Sample(sequence, nextFrame, nextFrame, 0, true), entry);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 10d / 60,
            new(action == 2, true, .095f, CrouchHeld: action == 4, InputMagnitude: action == 2 ? 1 : 0,
                ContactMotionLength: .01f));
        float expectedFraction = blendTicks switch
        {
            -2 => Rac1CrouchAnimation.StandUpBlend[0],
            -1 => Rac1OrdinaryStopAnimation.StandingBlend[0],
            _ => 1f / blendTicks,
        };
        Assert.Equal(source + (nextFrame - source) * expectedFraction,
            controller.Current.SampledPose!.LocalPositions[0], 6);
    }

    [Fact]
    public void RepeatedFlipLandingsKeepTheAirClipUntilNativeExitThenInterpolateStanding()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LandingAvatar());
        int tick = 0;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var flip = new Rac1MovementAnimationSelection(0xb, 28, attempt, BlendTicks: 5, Loop: true);
            for (int air = 0; air < 12; air++)
                controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick++ / 60d,
                    new(false, false, .001f, Traversal: flip));
            for (int contact = 1; contact <= 12; contact++)
            {
                controller.StepOrdinaryAnimation(contact == 1 ? PlayerAnimationState.Land : PlayerAnimationState.Walk,
                    tick++ / 60d, new(false, true, .001f, ContactMotionLength: .001f, Traversal: flip));
                Assert.Equal("28", controller.Current.SourceSequenceKey);
                Assert.NotNull(controller.Current.SampledPose);
                Assert.Null(controller.OrdinaryLocomotionSample);
            }
            double source = controller.Current.SampledPose!.LocalPositions[0] + .25d;
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick++ / 60d,
                new(false, true, 0, ContactMotionLength: 0, GroundEntry: new(0, 0, attempt, BlendTicks: -2)));
            Assert.Equal(source, controller.Current.SampledPose!.LocalPositions[0], 6);
            for (int standing = 1; standing <= 32; standing++)
            {
                controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick++ / 60d,
                    new(false, true, 0, ContactMotionLength: 0));
                Assert.Equal("0", controller.Current.SourceSequenceKey);
                Assert.NotNull(controller.OrdinaryStandingSample);
                Assert.NotNull(controller.Current.SampledPose);
            }
            Assert.Equal(2d, controller.Current.SampledPose!.LocalPositions[0], 6);
        }
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(9, 5)]
    [InlineData(2, -2)]
    public void NativeIdleHandoffWithoutSelectionPreservesAirClockAndCompletesIntoStanding(
        int transferTick, int entryBlend)
    {
        var controller = new Rac1PlayerAnimationPresentationController(LandingAvatar());
        var flip = new Rac1MovementAnimationSelection(0xb, 28, 1, BlendTicks: entryBlend, Loop: true);
        for (int tick = 0; tick <= transferTick; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick / 60d,
                new(false, false, 0, Traversal: flip));
        var expectedClock = new Rac1AnimationFrameClock(
            new Dictionary<int, IReadOnlyList<double>> { [28] = Enumerable.Repeat(1d / 15, 16).ToArray() },
            28, 0, 5);
        if (entryBlend == -2)
            expectedClock.SelectCrouchExit(28);
        for (int advance = 0; advance <= transferTick; advance++) expectedClock.Advance();
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, (transferTick + 1d) / 60,
            new(false, true, 0, ContactMotionLength: 0, GroundEntry: new(0, 28, 1, BlendTicks: 0)));
        Assert.Equal("28", controller.Current.SourceSequenceKey);
        Assert.Equal("0", controller.Current.SourceStateKey);
        var transferred = controller.OrdinaryStandingSample!.Value;
        Assert.Equal(expectedClock.Current, transferred);
        Assert.Equal(transferred.IsBlending ? 0 : transferred.CurrentFrame + transferred.Fraction,
            controller.Current.SampledPose!.LocalPositions[0], 6);
        int tickIndex = transferTick + 2;
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tickIndex++ / 60d,
            new(false, true, 0, ContactMotionLength: 0));
        Assert.Equal("28", controller.Current.SourceSequenceKey);
        Assert.Equal(entryBlend == -2 ? Rac1CrouchAnimation.StandUpBlend[transferTick + 1] :
            transferred.IsBlending ? transferred.Fraction + .2f : transferred.Fraction + .25f,
            controller.OrdinaryStandingSample!.Value.Fraction, 6);
        for (; tickIndex < 100 && controller.Current.SourceSequenceKey != "0"; tickIndex++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tickIndex / 60d,
                new(false, true, 0, ContactMotionLength: 0));
        Assert.Equal("0", controller.Current.SourceSequenceKey);
        Assert.Equal(new Rac1AnimationFrameClock.Sample(0, 0, 0, 0, true), controller.OrdinaryStandingSample);
        double source = controller.Current.SampledPose!.LocalPositions[0];
        for (int blend = 0; blend < 16; blend++)
        {
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tickIndex++ / 60d,
                new(false, true, 0, ContactMotionLength: 0));
            Assert.Equal(source * (1d - Rac1CrouchAnimation.StandUpBlend[blend]),
                controller.Current.SampledPose!.LocalPositions[0], 6);
        }
        Assert.False(controller.OrdinaryStandingSample!.Value.IsBlending);
    }

    [Fact]
    public void PreservedAirClipUnderIdleOwnershipCanRestartAndResetWithoutStalePose()
    {
        var controller = new Rac1PlayerAnimationPresentationController(LandingAvatar());
        controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, 0,
            new(false, false, 0, Traversal: new(0xb, 28, 1, BlendTicks: 5, Loop: true)));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, 1d / 60,
            new(false, true, 0, ContactMotionLength: 0, GroundEntry: new(0, 28, 1, BlendTicks: 0)));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Run, 2d / 60,
            new(true, true, .02f, InputMagnitude: 1, ContactMotionLength: .02f));
        Assert.Equal("28", controller.Current.SourceSequenceKey);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Run, 3d / 60,
            new(true, true, .02f, InputMagnitude: 1, ContactMotionLength: .02f));
        Assert.Equal("3", controller.Current.SourceSequenceKey);
        Assert.Equal("2", controller.Current.SourceStateKey);
        Assert.NotNull(controller.OrdinaryLocomotionSample);
        Assert.Null(controller.OrdinaryStandingSample);
        Assert.NotNull(controller.Current.SampledPose);
        controller.ResetPhysicsPresentation();
        Assert.Null(controller.OrdinaryStandingSample);
        Assert.Null(controller.OrdinaryLocomotionSample);
        Assert.Null(controller.Current.SampledPose);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void NoSelectionIdleTransferRetainsCompletionWhileNonloopingAirClockFreezesItsTerminalPose(int frozenUpdates)
    {
        var avatar = LandingAvatar();
        avatar = avatar with
        {
            AnimationClips = avatar.AnimationClips.Select(clip => clip.Id == "directional-flip-forward-right"
                ? Clip(clip.Id, clip.Role, [1d / 30, 1d / 30, 1d / 30]) with
                { FrameTimelinePositions = [0, 1, 2] }
                : clip).ToArray(),
        };
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        for (int tick = 0; tick <= 4 + frozenUpdates; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Fall, tick / 60d,
                new(false, false, 0, Traversal: new(0xb, 28, 1, BlendTicks: 1)));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, (5d + frozenUpdates) / 60,
            new(false, true, 0, ContactMotionLength: 0, GroundEntry: new(0, 28, 1, BlendTicks: 0)));
        Assert.Equal("28", controller.Current.SourceSequenceKey);
        Assert.Equal(2d, controller.Current.SampledPose!.LocalPositions[0]);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, (6d + frozenUpdates) / 60,
            new(false, true, 0, ContactMotionLength: 0));
        Assert.Equal("0", controller.Current.SourceSequenceKey);
        Assert.True(controller.OrdinaryStandingSample!.Value.IsBlending);
        Assert.Equal(2d, controller.Current.SampledPose!.LocalPositions[0]);
    }
}
