namespace OBP.RAC1.Player;

/// <summary>
/// The regular-axis jump impulse/gravity path at 0x2142d4..0x214638.
/// Start copies the contact-processed vertical step, as native entry copies
/// P+0x110 into P+0xe0, and resets pending/applied impulses independently.
/// Host collision and special planar ownership remain separate.
/// </summary>
public sealed class Rac1AirborneMotionSession
{
    private readonly Rac1AirborneJumpProfile _profile;
    private float _pendingImpulse;
    private float _appliedImpulse;
    private int _scriptIndex = -1;
    private int _scriptAge;
    private float _scriptStep;
    private bool _ceilingBlocked;

    public Rac1AirborneMotionSession(Rac1AirborneJumpProfile profile, float contactVerticalStep)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateFinite(contactVerticalStep, nameof(contactVerticalStep));
        // Only factory-produced native profiles are admitted by this solver.
        if (profile != Rac1AirborneJumpProfile.Create(profile.Kind,
                profile.EntryFrame == 1 || profile.LaunchAge == 4,
                profile.EntrySequence is >= 28 and <= 31 ? profile.EntrySequence - 28 : 2,
                profile.Kind == Rac1AirborneJumpKind.HeliPackHigh && profile.ScriptStartAge == 34,
                profile.EnvironmentDepth))
            throw new ArgumentException("Unknown native airborne profile.", nameof(profile));
        _profile = profile;
        HeightParameter = profile.MinimumHeight;
        GravityPerUpdate = profile.GravityPerUpdate;
        VerticalStep = contactVerticalStep;
    }

    public Rac1AirborneJumpProfile Profile => _profile;
    public float HeightParameter { get; private set; }
    public float VerticalStep { get; private set; }
    public float GravityPerUpdate { get; private set; }
    public float AppliedImpulse => _appliedImpulse;
    public bool HasLaunched { get; private set; }
    public bool Finished { get; private set; }

    public readonly record struct StepInput(
        int NativeActionAge, bool CrossHeld, float ContactVerticalStep,
        bool HitCeiling = false, bool LandedAfterLaunch = false);

    public readonly record struct StepResult(
        float VerticalStep, bool HasLaunched, bool Finished, float HeightParameter);

    /// <summary>
    /// NativeActionAge is supplied explicitly: moving execution precedes the
    /// counter/action-selection pass. LandedAfterLaunch is an admitted host
    /// action interruption; a stale grounded bit during anticipation is not it.
    /// Ceiling interruption is adapter policy (the native contact solver is
    /// not reproduced here) and cannot generate a fresh upward impulse.
    /// </summary>
    public StepResult Step(StepInput input)
    {
        if (input.NativeActionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        ValidateFinite(input.ContactVerticalStep, nameof(input));
        if (Finished)
            return Result();
        if (input.LandedAfterLaunch && HasLaunched)
        {
            Finished = true;
            VerticalStep = 0;
            return Result();
        }
        if (input.HitCeiling)
        {
            _ceilingBlocked = true;
            _pendingImpulse = 0;
            VerticalStep = MathF.Min(VerticalStep, 0);
        }

        bool mayGrow = !_ceilingBlocked &&
            (input.CrossHeld || _profile.Kind == Rac1AirborneJumpKind.WaterExit ||
                (_pendingImpulse == 0 && _appliedImpulse == 0));
        if (mayGrow && input.NativeActionAge <= 15 && HeightParameter < _profile.MaximumHeight)
        {
            float increment = (_profile.MaximumHeight - _profile.MinimumHeight) / _profile.HeightGrowthTicks;
            HeightParameter = MathF.Min(HeightParameter + increment, _profile.MaximumHeight);
            float doubledHeight = HeightParameter + HeightParameter;
            float impulse = MathF.Sqrt(doubledHeight * GravityPerUpdate);
            _pendingImpulse += (impulse - _appliedImpulse) - _pendingImpulse;
        }
        if (!_ceilingBlocked && input.NativeActionAge >= _profile.LaunchAge && _pendingImpulse > 0)
        {
            VerticalStep += _pendingImpulse;
            _appliedImpulse += _pendingImpulse;
            _pendingImpulse = 0;
            HasLaunched = true;
        }

        if (!_ceilingBlocked)
            ApplyScript(input.NativeActionAge);
        if (_profile.Kind == Rac1AirborneJumpKind.ThrusterPackLong && input.NativeActionAge > 54)
        {
            float target = 25f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate;
            float amount = 7f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate;
            GravityPerUpdate += Math.Clamp(target - GravityPerUpdate, -amount, amount);
        }

        if (_profile.Kind == Rac1AirborneJumpKind.LedgeJump && input.NativeActionAge < 10)
            return Result();
        if (input.NativeActionAge < _profile.LaunchAge)
        {
            // Native vertical is zeroed and the support request is subtracted.
            VerticalStep = -(48f * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate);
        }
        else
        {
            VerticalStep -= GravityPerUpdate;
            VerticalStep = MathF.Max(VerticalStep, input.ContactVerticalStep - 0.1f);
            VerticalStep = MathF.Max(VerticalStep, -(50f * Rac1AirborneJumpProfile.SecondsPerUpdate));
        }
        return Result();
    }

    private void ApplyScript(int age)
    {
        if (_profile.ScriptEndAge <= _profile.ScriptStartAge ||
            age < _profile.ScriptStartAge || age >= _profile.ScriptEndAge)
            return;
        _scriptAge++;
        while (_scriptIndex < 0 || _scriptAge >= ScriptSegment(_scriptIndex).Ticks)
        {
            _scriptIndex++;
            _scriptAge = 0;
            var segment = ScriptSegment(_scriptIndex);
            if (segment.StartCoefficient is { } coefficient)
                _scriptStep = coefficient * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate;
        }
        if (_scriptAge > 0)
            _scriptStep += ScriptSegment(_scriptIndex).AccelerationCoefficient *
                Rac1AirborneJumpProfile.SquaredSecondsPerUpdate;
        VerticalStep += _scriptStep;
    }

    private (float? StartCoefficient, float AccelerationCoefficient, int Ticks) ScriptSegment(int index)
    {
        if (_profile.Kind == Rac1AirborneJumpKind.WaterExit)
            return index switch
            {
                0 => (-27f, 0f, 9),
                1 => (40f, 0f, 1),
                2 => (55f, 0f, 1),
                3 => (75f, 0f, 1),
                4 => (90.5f, 0f, 9),
                5 => (75f, 0f, 4),
                6 => (40f, 0f, 3),
                7 => (0f, 0f, 100),
                _ => throw new InvalidOperationException("Water-exit script escaped its native age range."),
            };
        if (_profile.Kind == Rac1AirborneJumpKind.HeliPackHigh)
            return (48f, 0f, 1000);
        return index switch
        {
            0 => (151f, 0f, 5),
            1 => (0f, 0f, 27),
            2 => (22f, 2f, 6),
            3 => (null, -3f, 6),
            4 => (0f, 2f, 99),
            _ => throw new InvalidOperationException("Airborne script escaped its admitted age range."),
        };
    }

    private StepResult Result() => new(VerticalStep, HasLaunched, Finished, HeightParameter);

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name);
    }
}
