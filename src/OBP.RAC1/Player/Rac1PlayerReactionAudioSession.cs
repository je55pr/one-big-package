using OBP.RAC1.Animation;

namespace OBP.RAC1.Player;

/// <summary>
/// Standard enemy-hit audio: state16 selects sequence16/frame3 with blend -3
/// (0x22667c/227188). The preset has two remaining blend updates after entry
/// consumes .5; it then advances the decoded clip. Zero Nanotech follows the
/// regular-floor state3d/sequence69 after28 updates (0x22f858). This audio-only
/// projection does not implement the missing native damage motion/pose owner.
/// </summary>
public sealed class Rac1PlayerReactionAudioSession(IReadOnlyDictionary<int, Rac1AnimationSoundClip> clips)
{
    private readonly Rac1AnimationSoundCursor _cursor = new(clips);
    private Rac1AnimationFrameClock? _clock;
    private int _age;
    private bool _fatal;
    private long _generation;

    public bool NotifyDamage(int before, int after)
    {
        if (before <= after || after < 0) return false;
        _fatal = after == 0;
        _age = 0;
        Select(16, 3, 2);
        return true;
    }

    public int? Step()
    {
        if (_clock is null) return null;
        if (_fatal && ++_age == 28) Select(69, 0, 12);
        var sample = _clock.Advance();
        int? sound = _cursor.Step(sample, _generation);
        if (!sample.IsBlending && sample.NextFrame == 0) _clock = null;
        return sound;
    }

    public void Reset() { _clock = null; _cursor.Reset(); _age = 0; _fatal = false; }

    private void Select(int sequence, int frame, int blend)
    {
        if (!clips.TryGetValue(sequence, out var clip) || clip.FrameDurations is not { } durations)
            throw new InvalidDataException("R&C1 player reaction sound timeline is absent.");
        _clock = new(new Dictionary<int, IReadOnlyList<double>> { [sequence] = durations }, sequence, frame, blend);
        _cursor.Reset();
        _cursor.Step(_clock.Current, ++_generation);
    }
}
