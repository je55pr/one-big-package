namespace OBP.RAC1.Player;

/// <summary>
/// Bounded mode-0 action-2 release and action-3 restart predicates. Inputs are
/// native P+0x229c (previous update's capped magnitude), P+0x160 (3D primary
/// contact-motion length), and the unclamped magnitude history at 0x266e80(4).
/// Caller equipment, aiming and action admission must already be satisfied.
/// </summary>
public static class Rac1OrdinaryStopAnimation
{
    public enum Release { KeepLocomotion, DefaultStop, MiddleStop, Standing }
    public enum Restart { KeepStop, StandingWithoutSelection, Locomotion }

    public const float ReleaseInputThreshold = 0.17f;
    public const float LowMotionThreshold = 2.7f * (1f / 60f);
    public const float HighMotionThreshold = 4.4f * (1f / 60f);
    public const float RecentInputThreshold = 0.8f;
    public const float RestartInputThreshold = 0.2f;
    public const float MovingRestartInputThreshold = 0.5f;
    public const float IdleRestartInputThreshold = 0.22f;

    public static IReadOnlyList<float> StandingBlend { get; } = Array.AsReadOnly(new[]
    {
        .15f, .29f, .42f, .53f, .63f, .72f, .80f, .87f, .93f, .97f, 1f,
    });

    public static Release SelectRelease(float delayedInput, float contactMotionLength,
        float recentInput, int actionAge)
    {
        Validate(delayedInput, 1, nameof(delayedInput));
        Validate(contactMotionLength, float.MaxValue, nameof(contactMotionLength));
        Validate(recentInput, 2, nameof(recentInput));
        if (actionAge < 0)
            throw new ArgumentOutOfRangeException(nameof(actionAge));
        if (delayedInput >= ReleaseInputThreshold)
            return Release.KeepLocomotion;
        if (contactMotionLength > LowMotionThreshold)
            return contactMotionLength < HighMotionThreshold || recentInput < RecentInputThreshold
                ? Release.MiddleStop : Release.DefaultStop;
        return actionAge > 25 ? Release.Standing : Release.KeepLocomotion;
    }

    public static Restart SelectRestart(float delayedInput, float scalar)
    {
        Validate(delayedInput, 1, nameof(delayedInput));
        Validate(scalar, float.MaxValue, nameof(scalar));
        if (delayedInput <= RestartInputThreshold)
            return Restart.KeepStop;
        if (scalar == 0)
            return Restart.StandingWithoutSelection;
        return delayedInput > MovingRestartInputThreshold ? Restart.Locomotion : Restart.KeepStop;
    }

    /// <summary>Veldin 0x24edc8, including snapshot blends and frame wrapping.</summary>
    public static float EvaluateAnimationTime(Rac1AnimationFrameClock.Sample sample,
        IReadOnlyList<double> timeline)
    {
        if ((uint)sample.CurrentFrame >= (uint)timeline.Count || (uint)sample.NextFrame >= (uint)timeline.Count ||
            !float.IsFinite(sample.Fraction) || sample.Fraction < 0 || sample.Fraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(sample));
        float current = (float)timeline[sample.CurrentFrame], next = (float)timeline[sample.NextFrame];
        if (!float.IsFinite(current) || !float.IsFinite(next))
            throw new ArgumentException("Timeline must have finite authored positions.", nameof(timeline));
        if (sample.IsBlending)
            return next + sample.Fraction;
        if (sample.NextFrame < sample.CurrentFrame)
            return current + sample.Fraction;
        return current * (1f - sample.Fraction) + next * sample.Fraction;
    }

    private static void Validate(float value, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Native 30-entry magnitude ring: head is the next write slot.</summary>
public sealed class Rac1InputMagnitudeHistory
{
    private readonly float[] _values = new float[30];
    private int _head;
    public int Count { get; private set; }

    public void Append(float magnitude)
    {
        if (!float.IsFinite(magnitude) || magnitude < 0 || magnitude > 2)
            throw new ArgumentOutOfRangeException(nameof(magnitude));
        _values[_head] = magnitude;
        _head = (_head + 1) % _values.Length;
        Count = Math.Min(Count + 1, _values.Length);
    }

    public float Read(int updatesBack)
    {
        if (updatesBack is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(updatesBack));
        int back = Math.Min(updatesBack, Count);
        return _values[(_head - back + _values.Length) % _values.Length];
    }

    public void Reset()
    {
        Array.Clear(_values);
        _head = Count = 0;
    }
}
