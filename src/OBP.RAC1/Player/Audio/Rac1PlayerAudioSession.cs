using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

/// <summary>
/// Player sound dispatch recovered from Veldin's animation advance, footsteps
/// (0x206080), action-8 pack slots (0x21e8cc), and class71 (0x2a84c8).
/// The host supplies its already resolved animation/contact and attack facts.
/// </summary>
public sealed class Rac1PlayerAudioSession
{
    public readonly record struct Cue(int Class, int Sound, bool LevelSound = false);
    public readonly record struct Facts(Rac1AnimationFrameClock.Sample Animation, double Selection,
        int NativeAction, int ActionAge, int SurfaceEffectMode, int PackId, bool Grounded, bool Magneboots = false,
        int? MovementAction = null, float FractionAdvance = 0);
    public sealed record Frame(IReadOnlyList<Cue> OneShots, int? PackLoop, int? AnimationLoop);
    public sealed record WrenchFrame(IReadOnlyList<Cue> OneShots, int? Loop);

    // Bytes at Veldin 0x1bd820; the ordinary level sound prefix at 0x15f574 is 2.
    private static ReadOnlySpan<byte> FootstepBases => [0, 2, 6, 1, 0, 2, 2, 0, 2, 2, 1, 7, 0, 0, 4, 0, 0, 0, 0];
    private readonly IReadOnlyDictionary<int, Rac1AnimationSoundClip> _clips;
    private readonly Rac1AnimationSoundCursor _animation;
    private Facts? _previous;
    private long _wrenchGeneration = -1;
    private bool _wrenchHit, _wrenchWasThrown, _wrenchLoopStopped;
    private int _wrenchHitDelay, _wrenchState;
    private float? _wrenchTime;
    public bool WrenchImpactAdmitted => !_wrenchHit || _wrenchState is 10 or 11 && _wrenchHitDelay == 0;

    public Rac1PlayerAudioSession(IReadOnlyDictionary<int, Rac1AnimationSoundClip> clips)
    {
        _clips = clips;
        _animation = new(clips);
    }

    public Frame Step(int level, Facts facts, long wrenchGeneration)
    {
        var cues = new List<Cue>();
        if (_animation.Step(facts.Animation, facts.Selection) is int sound) cues.Add(new(0, sound));
        if (facts.NativeAction == 2 && facts.ActionAge >= 15)
        {
            if (facts.ActionAge == 22) Foot(0);
            if (!facts.Animation.IsBlending && _previous is { } old &&
                old.Animation.Sequence == facts.Animation.Sequence &&
                _clips.TryGetValue(facts.Animation.Sequence, out var clip))
            {
                float now = Rac1OrdinaryStopAnimation.EvaluateAnimationTime(facts.Animation, clip.Timeline);
                float before = Rac1OrdinaryStopAnimation.EvaluateAnimationTime(old.Animation, clip.Timeline);
                // P+0xaac uses the fractional advance when authored time wraps,
                // so the early run step can still fire on that same update.
                float window = before <= now ? now - before : facts.FractionAdvance;
                if (facts.Animation.Sequence == 3) { Cross(49.5f, 0); Cross(17, 1); }
                else if (facts.Animation.Sequence == 4) { Cross(12.5f, 0); Cross(1, 1); }
                void Cross(float at, int foot) { if (at < now && now - at <= window) Foot(foot); }
            }
        }
        if (_previous is { NativeAction: 6 or 0x2d, Grounded: false } && facts.Grounded)
        { Foot(0); Foot(1); }
        _previous = facts;
        if (wrenchGeneration != _wrenchGeneration)
        {
            _wrenchGeneration = wrenchGeneration;
            _wrenchHit = false;
            _wrenchTime = null;
        }
        if (_wrenchHitDelay > 0) _wrenchHitDelay--;
        int? pack = (facts.MovementAction ?? facts.NativeAction) switch
        {
            8 when facts.PackId == 2 => 2,
            8 when facts.PackId == 3 => 18,
            0x31 => level == 15 ? 29 : 6,
            0x35 => 19,
            _ => null
        };
        int? direct = _clips.TryGetValue(facts.Animation.Sequence, out var selected) ? selected.DirectSound : null;
        return new(cues, pack, direct);

        void Foot(int foot) => cues.Add(new(0, FootstepIndex(level, facts.SurfaceEffectMode, foot, facts.Magneboots), true));
    }

    public WrenchFrame StepWrench(int nativeAction, float authoredTime, bool blending, int thrownState, bool nearCatch)
    {
        var cues = new List<Cue>();
        bool thrown = thrownState is 10 or 11;
        if (nativeAction == 0x14 && !blending && _wrenchTime is float before && before <= 27 && 27 < authoredTime)
            cues.Add(new(71, 2)); // Native hyper-strike ground probe sounds even without a damaged target.
        _wrenchTime = blending ? null : authoredTime;
        if (_wrenchWasThrown && !thrown) cues.Add(new(0, 5));
        if (!_wrenchWasThrown && thrown) _wrenchLoopStopped = false;
        _wrenchLoopStopped |= nearCatch;
        _wrenchWasThrown = thrown;
        _wrenchState = thrownState;
        return new(cues, thrown && !_wrenchLoopStopped ? 14 : null);
    }

    /// <summary>One impact per swing; the thrown wrench may sound again after the native 30-update gate.</summary>
    public Cue? WrenchContact(bool soft, bool world = false)
    {
        if (_wrenchHit && (_wrenchState is not (10 or 11) || _wrenchHitDelay > 0)) return null;
        _wrenchHit = true;
        _wrenchHitDelay = 30;
        return new(71, world ? 2 : soft ? 1 : 0);
    }

    public void Reset()
    {
        _animation.Reset(); _previous = null;
        _wrenchGeneration = -1; _wrenchState = _wrenchHitDelay = 0;
        _wrenchHit = _wrenchWasThrown = _wrenchLoopStopped = false; _wrenchTime = null;
    }

    public static int FootstepIndex(int level, int surface, int foot, bool magneboots = false)
    {
        if ((uint)level >= FootstepBases.Length || (uint)surface > 3 || (uint)foot > 1)
            throw new ArgumentOutOfRangeException(nameof(level));
        return 2 + FootstepBases[level] + surface * 4 + foot * 2 + (magneboots ? 1 : 0);
    }

    // Opening Veldin spawn explicitly writes common hit-record+9: 1 for
    // class749, 0 for class1440. Authored PVar bytes are not the initialized record.
    public static bool? WrenchVictimSoftness(int nativeClass) => nativeClass switch
    { Rac1ClassIds.VeldinToad => true, Rac1ClassIds.VeldinFlameRobot => false, _ => null };
}
