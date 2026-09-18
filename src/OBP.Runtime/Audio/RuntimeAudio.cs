namespace OBP.Runtime.Audio;

public enum RuntimeAudioCategory
{
    Music,
    Ambience,
    SoundEffect,
}

/// <summary>
/// Engine-neutral decoded audio. Samples are signed interleaved PCM16 frames;
/// native container ids, codec flags and decode semantics stop at the source-game
/// boundary before this value is constructed.
/// </summary>
public sealed record RuntimeAudioClip
{
    public int SampleRateHz { get; }
    public int ChannelCount { get; }
    public short[] InterleavedPcm16 { get; }

    public long FrameCount => InterleavedPcm16.LongLength / ChannelCount;
    public double DurationSeconds => (double)FrameCount / SampleRateHz;

    public RuntimeAudioClip(
        int sampleRateHz,
        int channelCount,
        short[] interleavedPcm16)
    {
        if (sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        if (channelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCount));
        ArgumentNullException.ThrowIfNull(interleavedPcm16);
        if (interleavedPcm16.LongLength % channelCount != 0)
        {
            throw new ArgumentException(
                "Interleaved PCM sample count must contain complete audio frames.",
                nameof(interleavedPcm16));
        }

        SampleRateHz = sampleRateHz;
        ChannelCount = channelCount;
        InterleavedPcm16 = interleavedPcm16;
    }
}

/// <summary>
/// Optional source position for spatial playback, expressed in OBP Y-up world
/// space. A null position on playback intent means non-positional audio.
/// </summary>
public sealed record RuntimeAudioPosition(double X, double Y, double Z);

/// <summary>
/// One engine-independent request to present decoded audio. The source-game layer
/// decides which native asset becomes <see cref="Clip"/> and whether it should
/// loop; the host decides how to realize the intent in its audio engine.
/// </summary>
public sealed record RuntimeAudioPlaybackIntent
{
    public RuntimeAudioClip Clip { get; }
    public RuntimeAudioCategory Category { get; }
    public bool Loop { get; }
    public double Gain { get; }
    public RuntimeAudioPosition? Position { get; }

    public bool IsPositional => Position is not null;

    public RuntimeAudioPlaybackIntent(
        RuntimeAudioClip clip,
        RuntimeAudioCategory category,
        bool loop = false,
        double gain = 1d,
        RuntimeAudioPosition? position = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!double.IsFinite(gain) || gain < 0d)
            throw new ArgumentOutOfRangeException(nameof(gain));

        Clip = clip;
        Category = category;
        Loop = loop;
        Gain = gain;
        Position = position;
    }
}
