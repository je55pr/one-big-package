using OBP.PS2.Audio;
using OBP.Runtime.Audio;

namespace OBP.RAC2.Audio;

/// <summary>
/// GC-native audio decoded into the game-neutral runtime contract. Asset
/// selection remains in RAC2; no SBlk ids, VAG metadata or GC selectors cross
/// into the shared runtime layer.
/// </summary>
public static class GcRuntimeAudio
{
    /// <summary>
    /// Decode the environment-selected stereo music pair. Playback is one linear
    /// pass: raw VAG trailer markers are preserved by the codec research but are
    /// not yet promoted into a semantic music-loop policy.
    /// </summary>
    public static RuntimeAudioPlaybackIntent DecodeLevelMusic(
        GcLevelAudioWad audio,
        short musicTrack)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var (left, right) = audio.ResolveMusicPair(musicTrack);
        RuntimeAudioClip clip = DecodeStereoPair(left, right);
        return new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.Music);
    }

    /// <summary>
    /// Decode the level's self-describing native upgrade VAG as a neutral
    /// one-shot. This deliberately does not associate it with any gameplay
    /// event; the application may use it only as a representative integration
    /// cue until event-to-sound mapping is recovered.
    /// </summary>
    public static RuntimeAudioClip DecodeRepresentativeOneShot(GcLevelAudioWad audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return DecodeMonoVag(audio.OpenUpgradeSample());
    }

    public static RuntimeAudioClip DecodeMonoVag(Vagp.Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.Header.ChannelCount != 1)
        {
            throw new InvalidDataException(
                $"GC VAG '{stream.Header.Name}' declares {stream.Header.ChannelCount} channels; expected mono.");
        }

        return new RuntimeAudioClip(
            stream.Header.SampleRate,
            channelCount: 1,
            stream.DecodeMono());
    }

    public static RuntimeAudioClip DecodeStereoPair(Vagp.Stream left, Vagp.Stream right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Header.ChannelCount != 1 || right.Header.ChannelCount != 1)
        {
            throw new InvalidDataException("GC music pair must contain two mono VAG streams.");
        }

        if (left.Header.SampleRate != right.Header.SampleRate)
        {
            throw new InvalidDataException(
                $"GC music pair sample-rate mismatch: {left.Header.SampleRate} Hz vs {right.Header.SampleRate} Hz.");
        }

        short[] leftPcm = left.DecodeMono();
        short[] rightPcm = right.DecodeMono();
        if (leftPcm.Length != rightPcm.Length)
        {
            throw new InvalidDataException(
                $"GC music pair PCM-length mismatch: {leftPcm.Length} vs {rightPcm.Length} samples.");
        }

        var interleaved = new short[checked(leftPcm.Length * 2)];
        for (int i = 0; i < leftPcm.Length; i++)
        {
            interleaved[i * 2] = leftPcm[i];
            interleaved[i * 2 + 1] = rightPcm[i];
        }

        return new RuntimeAudioClip(
            left.Header.SampleRate,
            channelCount: 2,
            interleaved);
    }
}
