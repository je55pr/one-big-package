using OBP.RAC1.Player;

namespace OBP.RAC1.Animation;

/// <summary>The sound portion of a sequence; keyframe coordinates are distinct from its authored motion timeline.</summary>
public sealed record Rac1AnimationSoundClip(int Sequence, int? DirectSound,
    IReadOnlyList<Rac1MobyAnimation.TimedSoundCue> Cues, IReadOnlyList<double> Timeline,
    IReadOnlyList<double>? FrameDurations = null)
{
    public static Rac1AnimationSoundClip From(Rac1MobyAnimation.Sequence sequence) => new(
        sequence.Index, sequence.HasDirectSound ? sequence.SoundId : null,
        sequence.TimedSoundCues, sequence.Frames.Select(frame => frame.TimestampUnits / 16d).ToArray(),
        sequence.Frames.Select(frame => sequence.Frames.Count == 1 ? 1d / 60d :
            1d / (60d * (sequence.ConstantTransitionRate > 0 ? sequence.ConstantTransitionRate : frame.TransitionRate))).ToArray());
}

/// <summary>
/// Veldin 0x232670: while the source and target sequence agree, play the first
/// event in (old keyframe*16 + trunc(fraction*16), new position]. Do not replay
/// early events on a wrap, during a blend, or on a sequence/ownership reset.
/// </summary>
public sealed class Rac1AnimationSoundCursor(IReadOnlyDictionary<int, Rac1AnimationSoundClip> clips)
{
    private Rac1AnimationFrameClock.Sample? _previous;
    private double _selection;

    public int? Step(Rac1AnimationFrameClock.Sample sample, double selection)
    {
        var previous = _previous;
        bool same = previous is { } old && old.Sequence == sample.Sequence && _selection == selection;
        _previous = sample;
        _selection = selection;
        if (!same || previous!.Value.IsBlending || sample.IsBlending ||
            !clips.TryGetValue(sample.Sequence, out var clip)) return null;
        int before = Position(previous.Value), now = Position(sample);
        foreach (var cue in clip.Cues)
            if (before < cue.PositionUnits && cue.PositionUnits <= now) return cue.SoundId;
        return null;
    }

    public void Reset() { _previous = null; _selection = 0; }

    private static int Position(Rac1AnimationFrameClock.Sample sample) =>
        sample.CurrentFrame * 16 + (int)(sample.Fraction * 16);
}
