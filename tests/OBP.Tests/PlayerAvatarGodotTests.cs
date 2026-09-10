using OBP.Godot.Player;
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
    public void WalkAndRunReuseSustainedLocomotionWithoutRestartingClip()
    {
        var playback = new PlayerAvatarView.Playback(Avatar());
        IPlayerAnimationStateSink sink = playback;

        playback.SetClock(1.0);
        sink.SetAnimationState(PlayerAnimationState.Walk);
        double started = playback.ClipStartedAtSeconds;
        playback.SetClock(1.08);
        sink.SetAnimationState(PlayerAnimationState.Run);

        Assert.Equal(PlayerAnimationState.Run, sink.CurrentAnimationState);
        Assert.Equal(PlayerAvatarAnimationRole.SustainedLocomotion, playback.CurrentClip.Role);
        Assert.Equal(started, playback.ClipStartedAtSeconds);
    }

    [Fact]
    public void JumpRiseChoosesStationaryOrMovingClipFromLaunchContext()
    {
        var stationary = new PlayerAvatarView.Playback(Avatar());
        stationary.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal(PlayerAvatarAnimationRole.StationaryJump, stationary.CurrentClip.Role);

        var moving = new PlayerAvatarView.Playback(Avatar());
        moving.SetAnimationState(PlayerAnimationState.Run);
        moving.SetAnimationState(PlayerAnimationState.JumpRise);
        Assert.Equal(PlayerAvatarAnimationRole.MovingJump, moving.CurrentClip.Role);
    }

    [Fact]
    public void JumpRiseToFallKeepsSelectedAirborneClipAndClockOrigin()
    {
        var playback = new PlayerAvatarView.Playback(Avatar());
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
        var stationary = new PlayerAvatarView.Playback(Avatar());
        stationary.SetAnimationState(PlayerAnimationState.JumpRise);
        stationary.SetAnimationState(PlayerAnimationState.Fall);
        stationary.SetAnimationState(PlayerAnimationState.Land);
        Assert.Equal(PlayerAvatarAnimationRole.Standing, stationary.CurrentClip.Role);

        var moving = new PlayerAvatarView.Playback(Avatar());
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
        var playback = new PlayerAvatarView.Playback(Avatar());
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
    public void ViewImplementsAnimationStateSinkSeam()
    {
        Assert.True(typeof(IPlayerAnimationStateSink).IsAssignableFrom(typeof(PlayerAvatarView)));
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
            Clip("locomotion", PlayerAvatarAnimationRole.SustainedLocomotion, [0.1, 0.1]),
            Clip("stop-a", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("stop-b", PlayerAvatarAnimationRole.LocomotionStopVariant, [0.1]),
            Clip("jump-stationary", PlayerAvatarAnimationRole.StationaryJump, [0.1, 0.2, 0.3]),
            Clip("jump-moving", PlayerAvatarAnimationRole.MovingJump, [0.05, 0.1]),
            Clip("crouch", PlayerAvatarAnimationRole.Crouch, [0.1]),
            Clip("crouch-right", PlayerAvatarAnimationRole.CrouchTurnRight, [0.1]),
            Clip("crouch-left", PlayerAvatarAnimationRole.CrouchTurnLeft, [0.1]),
            Clip("attack", PlayerAvatarAnimationRole.PrimaryAttack, [0.1, 0.2]),
        ];
        return new PlayerAvatar(
            new PlayerAvatarIdentity("test", "test", "avatar", "model"),
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
