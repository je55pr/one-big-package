using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using Xunit;

namespace OBP.Tests;

public sealed class PlayerAvatarGodotTests
{
    private static readonly PlayerAvatarAxes Rac1Axes = new(
        PlayerAvatarAxisDirection.PositiveX,
        PlayerAvatarAxisDirection.PositiveY,
        PlayerAvatarAxisDirection.PositiveZ);

    [Fact]
    public void RecoveredRuntimeSpawnAddsOnlyHostGroundingLiftAndSceneMirror()
    {
        var spawn = new RuntimeSpawn(
            132.09,
            31.4266167,
            115.48,
            0.6627014875);

        RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(spawn);

        Assert.Equal((float)-spawn.X, pose.Position.X);
        Assert.Equal((float)(spawn.Y + RuntimeSpawnSceneAdapter.GroundingLift), pose.Position.Y);
        Assert.Equal((float)spawn.Z, pose.Position.Z);
        Assert.Equal((float)-spawn.Yaw, pose.SceneYaw);
    }

    [Fact]
    public void Rac1LocalAxesMapToGodotRightUpForwardAndAlignBase()
    {
        var mapped = PlayerAvatarView.ToGodotLocal(
            new PlayerAvatarPoint(1, 2, 3), Rac1Axes, baseHeight: 0.5, alignGeometricBase: true);
        Assert.Equal(1f, mapped.X);
        Assert.Equal(2.5f, mapped.Y);
        Assert.Equal(-2f, mapped.Z);
    }

    [Fact]
    public void ConvertFrameKeepsAllVerticesOnOneExplicitLocalTransform()
    {
        double[] source = [1, 2, 3, -4, 5, 6];
        var mapped = PlayerAvatarView.ConvertFrame(source, Rac1Axes);
        Assert.Equal(2, mapped.Length);
        Assert.Equal(new global::Godot.Vector3(1, 3, -2), mapped[0]);
        Assert.Equal(new global::Godot.Vector3(-4, 6, -5), mapped[1]);
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(1d, 0d)]
    [InlineData(-1d, 0d)]
    [InlineData(0d, -1d)]
    [InlineData(1d, 1d)]
    [InlineData(-1d, 1d)]
    [InlineData(1d, -1d)]
    [InlineData(-1d, -1d)]
    public void Rac1VisibleMeshFacingMatchesNativeFacingAndMovement(double inputX, double inputY)
    {
        double nativeYaw = Rac1RatchetYawController.BuildMovementTarget(inputX, inputY, controlYaw: 0d);
        float sceneYaw = PlayerAvatarFacing.NativeZUpYawToGodotSceneYaw(nativeYaw);
        // Ratchet's reconstructed neutral mesh visibly faces local +X.
        var visualForward = (new global::Godot.Basis(global::Godot.Vector3.Up, sceneYaw) *
                             global::Godot.Vector3.Right).Normalized();

        var nativeFacing = new Rac1WrenchCombatController().ResolveFirstSwingFacing(nativeYaw);
        var wrenchForward = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            nativeFacing.X, nativeFacing.Y).Normalized();

        var movement = new Rac1RatchetMovementController();
        var movementBasis = new PlayerPlanarBasis(0d, -1d, -1d, 0d);
        var nativeBasis = new PlayerPlanarBasis(-1d, 0d, 0d, 1d);
        var step = movement.Step(
            new PlayerControlIntent(inputX, inputY, false, false, false, movementBasis, nativeBasis),
            new PlayerContactFacts(true),
            _ => nativeYaw);
        var movementForward = new global::Godot.Vector3(
            (float)step.PlanarX, 0f, (float)step.PlanarY).Normalized();

        Assert.InRange((visualForward - wrenchForward).Length(), 0f, 0.00001f);
        Assert.InRange((visualForward - movementForward).Length(), 0f, 0.00001f);
    }

    [Fact]
    public void ValidationAcceptsMultipleClipsIncludingTwoUnresolvedStopVariants()
    {
        var avatar = Avatar();
        PlayerAvatarView.Validate(avatar);
        Assert.Equal(2, avatar.AnimationClips.Count(
            clip => clip.Role == PlayerAvatarAnimationRole.LocomotionStopVariant));
    }

    [Fact]
    public void ValidationRequiresMatchingSurfaceTextureAndSharedFrames()
    {
        var avatar = Avatar(textures: []);
        var error = Assert.Throws<InvalidDataException>(() => PlayerAvatarView.Validate(avatar));
        Assert.Contains("matching texture", error.Message);

        var clips = Avatar().AnimationClips.ToArray();
        int moving = Array.FindIndex(clips, clip => clip.Role == PlayerAvatarAnimationRole.MovingJump);
        clips[moving] = clips[moving] with
        {
            LocalFrames = [[0, 0, 0, 1, 0, 0]],
            FrameDurationsSeconds = [0.1],
        };
        var mismatched = Avatar(clips: clips);
        error = Assert.Throws<InvalidDataException>(() => PlayerAvatarView.Validate(mismatched));
        Assert.Contains("finite XYZ vertex stream", error.Message);
    }

    [Fact]
    public void VariableTimingSelectsExactFrameIntervalsWithoutFlatteningToFps()
    {
        var clip = new PlayerAvatarAnimationClip(
            "variable",
            PlayerAvatarAnimationRole.StationaryJump,
            [Frame(0), Frame(1), Frame(2)],
            [0.1, 0.2, 0.3]);

        Assert.True(clip.HasVariableTiming);
        Assert.Null(clip.ConstantFramesPerSecond);
        Assert.Equal(0, clip.FrameIndexAt(0.099, loop: false));
        Assert.Equal(1, clip.FrameIndexAt(0.1, loop: false));
        Assert.Equal(1, clip.FrameIndexAt(0.299, loop: false));
        Assert.Equal(2, clip.FrameIndexAt(0.3, loop: false));
        Assert.Equal(2, clip.FrameIndexAt(1.0, loop: false));
        Assert.Equal(0, clip.FrameIndexAt(0.6, loop: true));
    }

    [Fact]
    public void StandingCompatibilityClockUsesNeutralPerFrameTiming()
    {
        var avatar = Avatar();
        Assert.Equal(0, PlayerAvatarView.FrameIndexAt(avatar, 0));
        Assert.Equal(1, PlayerAvatarView.FrameIndexAt(avatar, 0.14));
        Assert.Equal(0, PlayerAvatarView.FrameIndexAt(avatar, 0.21));
    }

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
    public void ViewImplementsResolvedAnimationPresentationSinkSeam()
    {
        Assert.True(typeof(IPlayerAnimationPresentationSink).IsAssignableFrom(typeof(PlayerAvatarView)));
    }

    private static SourceAwarePlaybackHarness NewPlayback() => new(Avatar());

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

    [Fact]
    public void SampledPoseValidationRejectsWrongFrameShapeAndNonfiniteCoordinates()
    {
        var avatar = Avatar();
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        var playback = new PlayerAvatarView.Playback(avatar, controller.Current);
        foreach (var pose in new[]
        {
            new PlayerAvatarSampledPose(2, Frame(0)),
            new PlayerAvatarSampledPose(0, [0, 0, 0]),
            new PlayerAvatarSampledPose(0, Frame(double.NaN)),
        })
        {
            Assert.Throws<InvalidDataException>(() => playback.SetAnimationPresentation(
                controller.Current with { SampledPose = pose }));
        }
    }

    private static PlayerAvatar LocomotionAvatar()
    {
        var avatar = Avatar();
        return avatar with
        {
            AnimationClips = avatar.AnimationClips.Select(clip => clip.Role switch
            {
                PlayerAvatarAnimationRole.LocomotionStart => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 33).ToArray()),
                PlayerAvatarAnimationRole.SustainedLocomotion => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 30d, 23).ToArray()),
                PlayerAvatarAnimationRole.Crouch => Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 15).ToArray()),
                PlayerAvatarAnimationRole.CrouchTurnLeft or PlayerAvatarAnimationRole.CrouchTurnRight =>
                    Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15d, 7).ToArray()),
                _ => clip,
            }).ToArray(),
        };
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

    private static PlayerAvatar LandingAvatar()
    {
        var avatar = StopAvatar();
        return avatar with
        {
            AnimationClips = [.. avatar.AnimationClips.Select(clip => clip.Role == PlayerAvatarAnimationRole.Standing
                ? Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 7.5, 8).ToArray()) with
                    { FrameTimelinePositions = Enumerable.Range(0, 8).Select(frame => (double)frame * 4).ToArray() }
                : clip),
                Clip("directional-flip-forward-right", PlayerAvatarAnimationRole.JumpVariant,
                    Enumerable.Repeat(1d / 15, 16).ToArray()) with
                    { FrameTimelinePositions = Enumerable.Range(0, 16).Select(frame => (double)frame * 2).ToArray() }],
        };
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

    private static void StopStep(Rac1PlayerAnimationPresentationController controller, int tick,
        float magnitude, float scalar, float contactLength) =>
        controller.StepOrdinaryAnimation(magnitude > 0 ? PlayerAnimationState.Run : PlayerAnimationState.Walk,
            tick / 60d, new(magnitude >= .25f, true, scalar,
                InputMagnitude: magnitude, ContactMotionLength: contactLength, UncappedInputMagnitude: magnitude));

    private static PlayerAvatar StopAvatar()
    {
        var avatar = LocomotionAvatar();
        var clips = avatar.AnimationClips.Select(clip => clip.Role == PlayerAvatarAnimationRole.LocomotionStopVariant
            ? Clip(clip.Id, clip.Role, Enumerable.Repeat(1d / 15, 13).ToArray()) : clip).ToList();
        clips.Add(Clip("locomotion-stop-middle", PlayerAvatarAnimationRole.LocomotionStopVariant,
            Enumerable.Repeat(1d / 15, 14).ToArray()));
        return avatar with
        {
            AnimationClips = clips.Select(clip => clip with
            {
                FrameTimelinePositions = Enumerable.Range(0, clip.FrameCount)
                    .Select(frame => (double)frame * (clip.Role == PlayerAvatarAnimationRole.SustainedLocomotion ? 1 : 2)).ToArray(),
            }).ToArray(),
        };
    }

    private sealed class SourceAwarePlaybackHarness
    {
        private readonly Rac1PlayerAnimationPresentationController _controller;
        private readonly PlayerAvatarView.Playback _playback;

        public SourceAwarePlaybackHarness(PlayerAvatar avatar)
        {
            _controller = new Rac1PlayerAnimationPresentationController(avatar);
            _playback = new PlayerAvatarView.Playback(avatar, _controller.Current);
        }

        public PlayerAnimationPresentation CurrentAnimationPresentation => _playback.CurrentAnimationPresentation;
        public PlayerAnimationState CurrentAnimationState => _playback.CurrentAnimationState;
        public PlayerAvatarAnimationClip CurrentClip => _playback.CurrentClip;
        public double ClipStartedAtSeconds => _playback.ClipStartedAtSeconds;
        public double ClipElapsedSeconds => _playback.ClipElapsedSeconds;
        public int CurrentFrame => _playback.CurrentFrame;

        public void SetAnimationState(PlayerAnimationState state) =>
            _playback.SetAnimationPresentation(_controller.SetAnimationState(state));

        public void SetClock(double clockSeconds)
        {
            _playback.SetClock(clockSeconds);
            _playback.SetAnimationPresentation(_controller.SetClock(clockSeconds));
        }
    }

    private static PlayerAvatar Avatar(
        IReadOnlyList<PlayerAvatarTexture>? textures = null,
        IReadOnlyList<PlayerAvatarAnimationClip>? clips = null)
    {
        var surface = new PlayerAvatarSurface("mat", 0, [0, 0, 1, 0, 0, 1], [0, 1, 2]);
        textures ??= [new PlayerAvatarTexture("mat", 0, 1, 1, [255, 255, 255, 255])];
        clips ??=
        [
            Clip("standing", PlayerAvatarAnimationRole.Standing, [0.1, 0.1]),
            Clip("locomotion-start", PlayerAvatarAnimationRole.LocomotionStart, [0.1, 0.1]),
            Clip("sustained-locomotion", PlayerAvatarAnimationRole.SustainedLocomotion, [0.1, 0.1]),
            Clip("locomotion-stop-a", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("locomotion-stop-b", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("stationary-jump", PlayerAvatarAnimationRole.StationaryJump, [0.1, 0.2, 0.3]),
            Clip("moving-jump", PlayerAvatarAnimationRole.MovingJump, [0.05, 0.1]),
            Clip("crouch", PlayerAvatarAnimationRole.Crouch, [0.1]),
            Clip("crouch-turn-right", PlayerAvatarAnimationRole.CrouchTurnRight, [0.1]),
            Clip("crouch-turn-left", PlayerAvatarAnimationRole.CrouchTurnLeft, [0.1]),
            Clip("primary-attack", PlayerAvatarAnimationRole.PrimaryAttack, [0.1, 0.2]),
        ];
        return new PlayerAvatar(
            new PlayerAvatarIdentity(ObpSourceGame.Synthetic, "test", "avatar", "model"),
            clips,
            [surface],
            textures,
            new PlayerAvatarBounds(new(0, 0, 0), new(1, 1, 1)),
            new PlayerAvatarBounds(new(0, 0, 0), new(2, 2, 2)),
            new PlayerAvatarPoint(0, 0, 0),
            0,
            Rac1Axes);
    }

    private static PlayerAvatarAnimationClip Clip(
        string id,
        PlayerAvatarAnimationRole role,
        IReadOnlyList<double> durations) =>
        new(id, role, durations.Select((_, index) => Frame(index)).ToArray(), durations);

    private static double[] Frame(double marker) =>
        [marker, 0, 0, 1 + marker, 0, 0, marker, 1, 0];
}
