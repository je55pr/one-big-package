namespace OBP.RAC1.Player;

/// <summary>Mode-0 action-4 animation rules from the verified Veldin overlay.</summary>
public static class Rac1CrouchAnimation
{
    public const int EntryFrame = 2;
    public const int MinimumReleaseAge = 11;
    public const float TurnEntryMagnitude = 0.2f;
    public const float TurnVelocityThreshold = 0.3490658402442932f * (1f / 60f);
    public const float TurnRateDivisor = 3.3161256313323975f * (1f / 60f);

    // 0x17be54, selected by 0x22c4b0 with native blend argument -2.
    public static IReadOnlyList<float> StandUpBlend { get; } = Array.AsReadOnly<float>(
        [0.10f, 0.20f, 0.29f, 0.38f, 0.46f, 0.54f, 0.61f, 0.68f,
         0.74f, 0.80f, 0.85f, 0.90f, 0.94f, 0.97f, 0.99f, 1f]);

    public static int EntryBlendTicks(bool previousPoseIsStanding) => previousPoseIsStanding ? 9 : 14;

    /// <summary>
    /// Uses signed native angular velocity P+0x184, not stick direction or a
    /// semantic left/right label. Small nonzero velocity retains the previous
    /// sequence/rate; exactly zero returns to 13. Null means no selector write.
    /// </summary>
    public static int? SelectTurn(int currentSequence, float inputMagnitude, float yawVelocity)
    {
        if (currentSequence is not (13 or 14 or 15))
            throw new ArgumentOutOfRangeException(nameof(currentSequence));
        if (!float.IsFinite(inputMagnitude) || inputMagnitude < 0 || inputMagnitude > 1)
            throw new ArgumentOutOfRangeException(nameof(inputMagnitude));
        if (!float.IsFinite(yawVelocity))
            throw new ArgumentOutOfRangeException(nameof(yawVelocity));
        if (inputMagnitude <= TurnEntryMagnitude && currentSequence == 13)
            return null;
        if (Math.Abs(yawVelocity) > TurnVelocityThreshold)
            return yawVelocity < 0 ? 15 : 14;
        return yawVelocity == 0 ? 13 : null;
    }

    public static float TurnPlaybackMultiplier(float yawVelocity)
    {
        if (!float.IsFinite(yawVelocity))
            throw new ArgumentOutOfRangeException(nameof(yawVelocity));
        return Math.Clamp(Math.Abs(yawVelocity) / TurnRateDivisor, 0.55f, 2.5f);
    }
}
