using System.Buffers.Binary;
using OBP.Godot;
using OBP.Runtime.Audio;

namespace OBP.Tests;

public sealed class RuntimeAudioHostTests
{
    [Fact]
    public void PrepareConvertsMonoPcmToLittleEndianGodotPayload()
    {
        var intent = new RuntimeAudioPlaybackIntent(
            new RuntimeAudioClip(48_000, 1, [short.MinValue, -1, 0, 1, short.MaxValue]),
            RuntimeAudioCategory.SoundEffect,
            gain: 0.5d);

        bool ok = RuntimeAudioHost.TryPrepare(intent, out var prepared, out var problem);

        Assert.True(ok, problem);
        Assert.NotNull(prepared);
        Assert.False(prepared!.Stereo);
        Assert.Equal(48_000, prepared.SampleRateHz);
        Assert.Equal(5, prepared.FrameCount);
        Assert.False(prepared.Loop);
        Assert.Equal(10, prepared.Pcm16LittleEndian.Length);
        Assert.Equal(short.MinValue,
            BinaryPrimitives.ReadInt16LittleEndian(prepared.Pcm16LittleEndian.AsSpan(0, 2)));
        Assert.Equal(-1,
            BinaryPrimitives.ReadInt16LittleEndian(prepared.Pcm16LittleEndian.AsSpan(2, 2)));
        Assert.InRange(prepared.VolumeDb, -6.021f, -6.020f);
    }

    [Fact]
    public void PreparePreservesStereoFramesLoopAndMapsPositionToGodotSpace()
    {
        var intent = new RuntimeAudioPlaybackIntent(
            new RuntimeAudioClip(44_100, 2, [1, 2, 3, 4]),
            RuntimeAudioCategory.Ambience,
            loop: true,
            position: new RuntimeAudioPosition(2, 3, 4));

        bool ok = RuntimeAudioHost.TryPrepare(intent, out var prepared, out var problem);

        Assert.True(ok, problem);
        Assert.NotNull(prepared);
        Assert.True(prepared!.Stereo);
        Assert.True(prepared.Loop);
        Assert.Equal(2, prepared.FrameCount);
        Assert.Equal(new global::Godot.Vector3(-2, 3, 4), prepared.ScenePosition);
    }

    [Theory]
    [InlineData(0d, -80f)]
    [InlineData(1d, 0f)]
    public void GainConversionIsFinite(double gain, float expected)
    {
        Assert.Equal(expected, RuntimeAudioHost.GainToDecibels(gain));
    }

    [Fact]
    public void PrepareRejectsUnsupportedOrEmptyClipsWithoutThrowing()
    {
        var surround = new RuntimeAudioPlaybackIntent(
            new RuntimeAudioClip(48_000, 3, [1, 2, 3]),
            RuntimeAudioCategory.Music);
        var empty = new RuntimeAudioPlaybackIntent(
            new RuntimeAudioClip(48_000, 1, []),
            RuntimeAudioCategory.SoundEffect);

        Assert.False(RuntimeAudioHost.TryPrepare(
            surround, out var surroundPrepared, out var surroundProblem));
        Assert.Null(surroundPrepared);
        Assert.Contains("3-channel", surroundProblem);

        Assert.False(RuntimeAudioHost.TryPrepare(
            empty, out var emptyPrepared, out var emptyProblem));
        Assert.Null(emptyPrepared);
        Assert.Contains("empty", emptyProblem);
    }
}
