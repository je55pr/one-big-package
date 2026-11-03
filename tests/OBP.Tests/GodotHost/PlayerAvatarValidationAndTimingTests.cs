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
/// Avatar clip validation, exact frame-interval timing and sampled-pose validation.
/// </summary>
public sealed class PlayerAvatarValidationAndTimingTests
{
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
}
