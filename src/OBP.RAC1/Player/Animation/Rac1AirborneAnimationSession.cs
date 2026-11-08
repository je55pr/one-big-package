namespace OBP.RAC1.Player;

/// <summary>
/// Completed regular-axis movement facts for the native airborne animation
/// selector. GroundClearance comes from the host's floor query; it bounds the
/// landing prediction rather than reproducing the native trajectory raycast.
/// </summary>
public readonly record struct Rac1AirborneAnimationInput(
    int NativeActionAge, float VerticalStep, float GravityPerUpdate,
    float GroundClearance, int LandingUpdates);

/// <summary>
/// Wall/Heli-Pack long-jump playback from Veldin 0x22ed84..0x22f224.
/// Keep the airborne target pose separate from the clip's landing recovery.
/// Other jump families and alternate gravity remain outside this producer.
/// </summary>
public sealed class Rac1AirborneAnimationSession
{
    private readonly int _launchAge;
    private readonly float _apexFrame, _landingTargetFrame;
    private const int LandingFrame = 10;
    private float _speed = 1;
    private bool _pastApex, _landing;
    public bool GlideHandoffReady { get; private set; }

    public Rac1AirborneAnimationSession(int nativeAction)
    {
        (_launchAge, _apexFrame, _landingTargetFrame) = nativeAction switch
        {
            0x0a => (5, 18f, 29f),
            0x11 => (9, 12f, 20f),
            _ => throw new ArgumentOutOfRangeException(nameof(nativeAction)),
        };
    }

    public readonly record struct Selection(float PlaybackMultiplier, int? EntryFrame = null, int BlendTicks = 0);

    public Selection Step(Rac1AirborneAnimationInput input, Rac1AnimationFrameClock.Sample sample,
        float authoredTime, float playbackRate, int frameCount)
    {
        Validate(input, authoredTime, playbackRate, frameCount);
        if (input.NativeActionAge <= _launchAge)
            return new(_speed);

        _pastApex |= input.VerticalStep < 0.001f;
        // OBP's held-long-jump handoff follows the completed rise/apex pose.
        // The landing target is retimed towards the floor and can be much
        // later over a drop; it must not delay deployment of the Heli-Pack.
        GlideHandoffReady |= _pastApex && authoredTime >= _apexFrame;
        if (input.NativeActionAge == _launchAge + 1)
        {
            // 0x233c30 truncates velocity/gravity; 0x208f20 retimes the
            // authored timeline, not the packed keyframe sound coordinates.
            int apexSteps = (int)(input.VerticalStep / input.GravityPerUpdate);
            _speed = MathF.Min(Retime(_apexFrame, apexSteps, sample, authoredTime, playbackRate, frameCount), 1.5f);
            return new(_speed);
        }
        if (input.LandingUpdates > 0)
        {
            bool selectRecovery = !_landing && _landingTargetFrame - 1.5f > authoredTime;
            _landing = true;
            _speed = 1;
            return selectRecovery ? new(_speed, LandingFrame + 2, 7) : new(_speed);
        }
        if (!_pastApex)
            return new(_speed);

        int? recovery = null;
        int blend = 0;
        if (sample.NextFrame < LandingFrame)
        {
            int landingSteps = PredictLandingSteps(input);
            float target = Retime(_landingTargetFrame, landingSteps, sample, authoredTime, playbackRate, frameCount);
            _speed += Math.Clamp(target - _speed, -0.2f, 0.2f);
            if (_speed > 1.8f)
            {
                recovery = LandingFrame + 2;
                blend = landingSteps + 3;
                _speed = 1; // Native sequence selection resets animation speed.
            }
            _speed = Math.Clamp(_speed, 0.2f, 2.7f);
        }
        if ((int)authoredTime == (int)_landingTargetFrame)
            _speed = 0;
        return new(_speed, recovery, blend);
    }

    private static void Validate(Rac1AirborneAnimationInput input, float authoredTime, float playbackRate, int frameCount)
    {
        if (input.NativeActionAge < 0 || input.LandingUpdates < 0 ||
            !float.IsFinite(input.VerticalStep) || !float.IsFinite(input.GravityPerUpdate) || input.GravityPerUpdate <= 0 ||
            !float.IsFinite(input.GroundClearance) || input.GroundClearance < 0 ||
            !float.IsFinite(authoredTime) || !float.IsFinite(playbackRate) || playbackRate <= 0 || frameCount <= LandingFrame + 2)
            throw new ArgumentOutOfRangeException(nameof(input));
    }

    private static int PredictLandingSteps(Rac1AirborneAnimationInput input)
    {
        // OBP floor-query approximation of 0x232b90's 60-update
        // trajectory query, preserving its discrete gravity equation.
        float b = input.VerticalStep + input.GravityPerUpdate * 0.5f;
        float steps = (b + MathF.Sqrt(b * b + 2 * input.GravityPerUpdate * input.GroundClearance)) /
            input.GravityPerUpdate;
        return steps > 0 ? (int)MathF.Min(60, steps) : 60;
    }

    private float Retime(float target, int steps, Rac1AnimationFrameClock.Sample sample,
        float authoredTime, float playbackRate, int frameCount)
    {
        if (steps <= 0) return _speed;
        float distance = target - authoredTime;
        if (distance <= 0)
            distance += (sample.IsBlending ? 2 : 0.5f / playbackRate) * frameCount;
        return 0.5f * distance / (steps * playbackRate);
    }
}
