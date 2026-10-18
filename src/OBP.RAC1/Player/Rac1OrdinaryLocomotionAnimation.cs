namespace OBP.RAC1.Player;

/// <summary>
/// Bounded recovery of Veldin 0x00228c18 for regular Ratchet, action 2,
/// substates 0/1, with the original 60 Hz timing context. The caller must
/// already have admitted the ordinary supported locomotion update.
/// See research/RAC1_LOCOMOTION_ANIMATION.md. These arithmetic rules do not
/// implement the native blend engine or all R5900 floating-point behavior.
/// </summary>
public static class Rac1OrdinaryLocomotionAnimation
{
    public const float NativeSecondsPerUpdate = 1f / 60f;
    public const int MinimumSubstateAge = 4;
    public const float RunEntryStep = 2.35f * NativeSecondsPerUpdate;
    public const float WalkReentryStep = 1.9f * NativeSecondsPerUpdate;

    /// <summary>
    /// P+0x194 is the scalar controller step, not host displacement after
    /// collision. P+0x1a0 counts updates since the substate changed. The
    /// P+0x3bc entry guard blocks selection but does not stop rate updates.
    /// Equality at either speed threshold retains the current sequence.
    /// </summary>
    public static int SelectSequence(
        int currentSequence, float scalarStep, int substateAge, bool entryGuard)
    {
        ValidateSequence(currentSequence);
        ValidateSpeed(scalarStep);
        // Above the final table row, the native search can read beyond the two
        // recovered rows before clamping. Do not invent an overspeed rule.
        if (scalarStep > 10f * NativeSecondsPerUpdate)
            throw new ArgumentOutOfRangeException(nameof(scalarStep));
        if (substateAge < 0)
            throw new ArgumentOutOfRangeException(nameof(substateAge));
        if (entryGuard || substateAge <= MinimumSubstateAge)
            return currentSequence;

        return currentSequence switch
        {
            3 when scalarStep > RunEntryStep => 4,
            4 when scalarStep < WalkReentryStep => 3,
            _ => currentSequence,
        };
    }

    /// <summary>
    /// P+0xa90 multiplies the decoded frame rate after the blend has completed.
    /// It is not a replacement for the clip's per-frame transition rate.
    /// </summary>
    public static float PlaybackMultiplier(int sequence, float scalarStep)
    {
        ValidateSequence(sequence);
        ValidateSpeed(scalarStep);
        return sequence == 3
            ? System.Math.Clamp(scalarStep * 157f, 0.6f, 4f)
            : System.Math.Clamp(scalarStep * 14f, 0.6f, 2.2f);
    }

    public static int EntryNextFrame(int sequence)
    {
        ValidateSequence(sequence);
        return sequence == 3 ? 8 : 0;
    }

    /// <summary>
    /// Align a sequence change from the native next-frame byte (Moby+0x51),
    /// not the currently displayed frame. Counts come from decoded assets.
    /// </summary>
    public static int TransitionNextFrame(
        int previousSequence, int previousNextFrame, int previousFrameCount,
        int nextSequence, int nextFrameCount)
    {
        ValidateSequence(previousSequence);
        ValidateSequence(nextSequence);
        if (previousFrameCount is < 1 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(previousFrameCount));
        if (nextFrameCount is < 1 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(nextFrameCount));
        if (previousNextFrame < 0 || previousNextFrame >= previousFrameCount)
            throw new ArgumentOutOfRangeException(nameof(previousNextFrame));

        int offset = previousSequence == nextSequence ? 0 : previousSequence == 3 ? 1 : 5;
        return ((previousNextFrame * nextFrameCount / previousFrameCount) + offset) % nextFrameCount;
    }

    private static void ValidateSequence(int sequence)
    {
        if (sequence is not (3 or 4))
            throw new ArgumentOutOfRangeException(nameof(sequence));
    }

    private static void ValidateSpeed(float scalarStep)
    {
        if (!float.IsFinite(scalarStep) || scalarStep < 0)
            throw new ArgumentOutOfRangeException(nameof(scalarStep));
    }
}
