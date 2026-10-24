namespace OBP.Runtime.Player;

/// <summary>
/// Cross-source presentation playback over source-supplied channels, masks and
/// blend parameters. The host supplies admitted use events and movement facts;
/// this does not replace any game's player controller or action admission.
/// </summary>
public sealed class PlayerEquipmentAnimationPlayer
{
    private PlayerEquipmentAnimationSource? _source;
    private PlayerSkeletalClip? _useClip;
    private readonly Dictionary<int, PlayerSkeletalClip> _armUseClips = [];
    private float _carryWeight, _useWeight;
    private double _useAge;
    private bool _using, _completed;
    public int? CurrentUseSequence => _useWeight > 0 ? _useClip?.SequenceId : null;
    public float UseWeight => _useWeight;

    public void Reset()
    {
        _source = null; _useClip = null; _armUseClips.Clear();
        _carryWeight = _useWeight = 0; _useAge = 0; _using = _completed = false;
    }

    public PlayerEquipmentPose Step(PlayerEquipmentAnimationSource source, int basisSequence,
        PlayerSkeletalSample basis, bool moving, bool crouching, bool useStarted, bool triggerHeld,
        PlayerSkeletalClip? basisClip = null)
    {
        if (!ReferenceEquals(source, _source)) { Reset(); _source = source; }
        if (source.UseBlendTicks < 1 || !float.IsFinite(source.BlendSteps.CarryIn) ||
            !float.IsFinite(source.BlendSteps.UseOut) || !float.IsFinite(source.BlendSteps.CompletedUseOut) ||
            source.BlendSteps.CarryIn is <= 0 or > 1 ||
            source.BlendSteps.UseOut is <= 0 or > 1 || source.BlendSteps.CompletedUseOut is <= 0 or > 1 ||
            source.ArmCount < 0 || source.ArmCount > source.Arms.Count)
            throw new InvalidDataException("Invalid source equipment presentation policy.");
        if (useStarted && (!_using || !source.SustainUse)) { _using = true; _completed = false; _useAge = 0; }
        if (_using && source.SustainUse && !triggerHeld) _using = false;
        int useSequence = crouching ? source.CrouchingUseSequence : moving ? source.MovingUseSequence : source.StandingUseSequence;
        if (_using && source.Body.TryGetValue(useSequence, out var selected))
        {
            _useClip = selected;
            if (!source.SustainUse && _useAge >= selected.DurationSeconds) { _using = false; _completed = true; }
        }
        _carryWeight = Math.Min(1, _carryWeight + source.BlendSteps.CarryIn);
        _useWeight = _using && useSequence >= 0 ? Math.Min(1, _useWeight + 1f / source.UseBlendTicks) :
            Math.Max(0, _useWeight - (_completed ? source.BlendSteps.CompletedUseOut : source.BlendSteps.UseOut));
        var layers = new List<PlayerSkeletalLayer>();
        if (basisSequence < source.ArmBankUpperExclusive)
            for (int side = 0; side < source.ArmCount; side++)
                if (source.Arms[side].TryGetValue(basisSequence, out var clip))
                {
                    var carrySample = basis;
                    // OBP cross-source policy: preserve cycle phase when the
                    // host clip has a different frame partition. Native games
                    // use the same clock directly within their own carrier.
                    if (basisClip is not null && !basisClip.Frames.Select(f => f.DurationSeconds)
                        .SequenceEqual(clip.Frames.Select(f => f.DurationSeconds)))
                    {
                        double phase = basisClip.Frames.Take(basis.Frame).Sum(f => f.DurationSeconds) +
                            basis.Fraction * basisClip.Frames[basis.Frame].DurationSeconds;
                        carrySample = clip.SampleAt(phase / basisClip.DurationSeconds * clip.DurationSeconds);
                    }
                    if ((uint)carrySample.Frame >= clip.Frames.Count || (uint)carrySample.NextFrame >= clip.Frames.Count)
                        throw new InvalidDataException("Carry animation needs an explicit source frame mapping.");
                    layers.Add(new(clip, carrySample, _carryWeight));
                }
        bool fullBody = _useWeight > 0 && !moving && !crouching;
        if (_useWeight > 0 && _useClip is not null && useSequence >= 0)
        {
            var clip = _useClip;
            if (!fullBody && !_armUseClips.TryGetValue(clip.SequenceId, out clip))
            {
                int[] joints = Enumerable.Range(0, source.ArmCount).SelectMany(side => source.Arms[side].First().Value.JointIndices).ToArray();
                clip = _useClip with
                {
                    JointIndices = joints,
                    Frames = _useClip.Frames.Select(f => f with { Joints = joints.Select(j => f.Joints[j]).ToArray() }).ToArray(),
                };
                _armUseClips.Add(clip.SequenceId, clip);
            }
            layers.Add(new(clip, clip.SampleAt(_useAge, source.SustainUse), _useWeight));
        }
        if (_using) _useAge += 1d / 60;
        return new(source.Skeleton, layers, source.HandJoint, fullBody);
    }
}
