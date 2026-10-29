using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PlayerAvatarClipSamplingTests
{
    private static PlayerAvatarAnimationClip Clip(params double[] durations) =>
        new("test", PlayerAvatarAnimationRole.Standing,
            durations.Select(_ => new double[3]).ToArray(), durations);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundoffImmediatelyBeforeFrameBoundarySnapsToValidNextFrame(bool loop)
    {
        var clip = Clip(0.1, 0.2, 0.125);
        double boundary = clip.FrameDurationsSeconds[0];
        double phase = Math.BitDecrement(boundary);
        Assert.Equal(1, clip.FrameIndexAt(phase, loop));

        var sample = clip.SampleAt(phase, loop);
        Assert.Equal(1, sample.Frame);
        Assert.Equal(2, sample.NextFrame);
        Assert.Equal(0, sample.Fraction);
    }

    [Fact]
    public void LoopEndRoundoffUsesFirstFrameRatherThanOutOfRangeFraction()
    {
        var clip = Clip(0.1, 0.2, 0.125);
        var wrapped = clip.SampleAt(Math.BitDecrement(clip.DurationSeconds), loop: true);
        Assert.Equal(0, wrapped.Frame);
        Assert.Equal(1, wrapped.NextFrame);
        Assert.Equal(0, wrapped.Fraction);

        var held = clip.SampleAt(clip.DurationSeconds + 3, loop: false);
        Assert.Equal(2, held.Frame);
        Assert.Equal(2, held.NextFrame);
        Assert.Equal(1, held.Fraction);
    }

    [Fact]
    public void SamplingPreservesInterpolatedSourceTimingAndRejectsInvalidInput()
    {
        var clip = Clip(0.1, 0.2, 0.125);
        var sample = clip.SampleAt(0.15, loop: true);
        Assert.Equal(1, sample.Frame);
        Assert.Equal(2, sample.NextFrame);
        Assert.Equal(.25f, sample.Fraction, 6);

        Assert.Throws<ArgumentOutOfRangeException>(() => clip.SampleAt(double.NaN, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.SampleAt(-1, true));
        Assert.Throws<InvalidDataException>(() => Clip(0.1, -0.2).SampleAt(0, true));
        Assert.Throws<InvalidDataException>(() => Clip().SampleAt(0, true));
    }

    [SkippableFact]
    public void RetailTaunterFrameBoundaryDoesNotSubmitInvalidSkeletalSample()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(path), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(path!, "ratchet");
        var controller = Assert.IsType<Rac1PlayerAnimationPresentationController>(
            Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar));
        var clip = avatar.AnimationClips.Single(clip =>
            clip.Id == Rac1PlayerAvatarProvider.AnimationDescriptor(51).Id);
        double phase = Math.BitDecrement(clip.FrameDurationsSeconds[0]);
        var sample = clip.SampleAt(phase, true);
        Assert.Equal(1, sample.Frame);
        Assert.Equal(0, sample.Fraction);

        var presented = controller.ApplyEquipmentPose(51, phase, loop: true, fullBody: true);
        Assert.NotNull(presented.SampledPose);
        Assert.All(presented.SampledPose!.LocalPositions, value => Assert.True(double.IsFinite(value)));
    }
}
