using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Animation;

/// <summary>Source-owned 60-Hz Moby sequence phase, authored positions and positive selector blends.</summary>
public sealed class Rac1MobySequenceClock
{
    private readonly Rac1MobyAnimationAssets.Asset _asset;
    private int _frame, _blendLeft, _blendTotal;
    private float _fraction;
    private RuntimeObjectAnimationSample? _previous;
    private bool _directSoundPending;
    public int Sequence { get; private set; }
    public bool Wrapped { get; private set; }
    public bool Blending => _blendLeft > 0;
    public float Multiplier { get; set; } = 1;
    public float Position { get; private set; }
    public RuntimeObjectAnimationSample Sample { get; private set; }
    public Vector3 ContactSocket => Vector3.Lerp(Clip.ContactSocket[_frame], Clip.ContactSocket[NextFrame], _fraction);
    private Rac1MobyAnimationAssets.Clip Clip => _asset.Clips[Sequence];
    private int NextFrame => (_frame + 1) % Clip.Rates.Length;

    public Rac1MobySequenceClock(Rac1MobyAnimationAssets.Asset asset, int sequence)
    {
        _asset = asset; Sequence = sequence;
        Sample = new(Clip.Id, 0, 0, 0);
        Select(sequence, 1);
    }

    public void Select(int sequence, int blendTicks = 10, int frame = 0)
    {
        if (!_asset.Clips.ContainsKey(sequence) || (uint)frame >= _asset.Clips[sequence].Rates.Length || blendTicks < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        _previous = Sample; Sequence = sequence; _frame = frame; _fraction = 0;
        _blendLeft = _blendTotal = blendTicks; Multiplier = 1; Position = Clip.Positions[frame];
        _directSoundPending = Clip.DirectSound.HasValue;
        Refresh();
    }

    public IReadOnlyList<int> Tick()
    {
        var sounds = new List<int>(); Wrapped = false;
        if (_directSoundPending) { sounds.Add(Clip.DirectSound!.Value); _directSoundPending = false; }
        if (_blendLeft > 0)
        {
            _blendLeft--; Refresh(); return sounds;
        }
        float before = Position;
        _fraction += Clip.Rates[_frame] * Multiplier;
        while (_fraction >= 1)
        {
            float excess = (_fraction - 1) / Clip.Rates[_frame];
            _frame = NextFrame;
            if (_frame == 0) Wrapped = true;
            _fraction = excess * Clip.Rates[_frame];
        }
        float end = NextFrame == 0 ? Clip.Positions[_frame] + .5f / Clip.Rates[_frame] : Clip.Positions[NextFrame];
        Position = Clip.Positions[_frame] + (end - Clip.Positions[_frame]) * _fraction;
        foreach (var cue in Clip.Sounds)
            if ((!Wrapped && cue.PositionFrames > before && cue.PositionFrames <= Position) ||
                Wrapped && (cue.PositionFrames > before || cue.PositionFrames <= Position)) sounds.Add(cue.SoundId);
        Refresh(); return sounds;
    }

    private void Refresh() => Sample = new(Clip.Id, _frame, Blending ? _frame : NextFrame, Blending ? 0 : _fraction,
        Blending ? _previous : null, Blending ? 1f - (float)_blendLeft / _blendTotal : 1);
}
