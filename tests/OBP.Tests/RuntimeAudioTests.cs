using OBP.Runtime.Audio;

namespace OBP.Tests;

public sealed class RuntimeAudioTests
{
    [Fact]
    public void DecodedClipReportsFrameCountAndDuration()
    {
        var clip = new RuntimeAudioClip(
            sampleRateHz: 48_000,
            channelCount: 2,
            interleavedPcm16: new short[96_000]);

        Assert.Equal(48_000L, clip.FrameCount);
        Assert.Equal(1d, clip.DurationSeconds);
        Assert.Equal(48_000, clip.SampleRateHz);
        Assert.Equal(2, clip.ChannelCount);
    }

    [Fact]
    public void DecodedClipRejectsInvalidFrameShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeAudioClip(0, 1, Array.Empty<short>()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeAudioClip(48_000, 0, Array.Empty<short>()));
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeAudioClip(48_000, 1, null!));
        Assert.Throws<ArgumentException>(
            () => new RuntimeAudioClip(48_000, 2, new short[3]));
    }

    [Fact]
    public void PlaybackIntentCoversLoopedMusicAndAmbienceWithoutNativeMetadata()
    {
        var clip = new RuntimeAudioClip(44_100, 2, new short[8]);
        var music = new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.Music,
            loop: true,
            gain: 0.75d);
        var ambience = new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.Ambience,
            loop: true);

        Assert.Same(clip, music.Clip);
        Assert.Equal(RuntimeAudioCategory.Music, music.Category);
        Assert.True(music.Loop);
        Assert.Equal(0.75d, music.Gain);
        Assert.False(music.IsPositional);
        Assert.Null(music.Position);

        Assert.Equal(RuntimeAudioCategory.Ambience, ambience.Category);
        Assert.True(ambience.Loop);
        Assert.False(ambience.IsPositional);
    }

    [Fact]
    public void OneShotEffectsMayBePositionalOrNonPositional()
    {
        var clip = new RuntimeAudioClip(48_000, 1, new short[28]);
        var position = new RuntimeAudioPosition(1.25d, 2.5d, -3.75d);
        var spatial = new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.SoundEffect,
            position: position);
        var nonSpatial = new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.SoundEffect);

        Assert.False(spatial.Loop);
        Assert.True(spatial.IsPositional);
        Assert.Equal(position, spatial.Position);
        Assert.False(nonSpatial.Loop);
        Assert.False(nonSpatial.IsPositional);
        Assert.Null(nonSpatial.Position);
    }

    [Fact]
    public void PlaybackIntentRejectsInvalidGain()
    {
        var clip = new RuntimeAudioClip(48_000, 1, Array.Empty<short>());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeAudioPlaybackIntent(
                clip, RuntimeAudioCategory.SoundEffect, gain: -0.01d));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeAudioPlaybackIntent(
                clip, RuntimeAudioCategory.SoundEffect, gain: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RuntimeAudioPlaybackIntent(
                clip, RuntimeAudioCategory.SoundEffect, gain: double.PositiveInfinity));
        Assert.Throws<ArgumentNullException>(
            () => new RuntimeAudioPlaybackIntent(
                null!, RuntimeAudioCategory.SoundEffect));
    }
}
