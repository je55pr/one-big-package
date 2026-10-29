namespace OBP.RAC1.Player;

/// <summary>
/// Retail-backed R&amp;C1 Ratchet sequence-selection contract.
/// This class records which native sequence is selected by recovered gameplay
/// state. It intentionally contains no frame counts, transition rates, or
/// playback durations; those belong to the separately decoded avatar clips.
/// </summary>
public static class Rac1RatchetSequenceSelection
{
    public const int StandingSequenceId = 0;
    public const int IdleFidgetASequenceId = 1;
    public const int IdleFidgetBSequenceId = 2;
    // Historical API names: 3 is also the steady low-speed loop, and 4 is the
    // high-speed loop. Entry order does not imply a timed one-shot transition.
    // Recovered scalar-speed/rate rules live in Rac1OrdinaryLocomotionAnimation.
    public const int LocomotionStartSequenceId = 3;
    public const int SustainedLocomotionSequenceId = 4;
    public const int LocomotionStopEarlySequenceId = 5;
    public const int LocomotionStopLateSequenceId = 6;
    public const int StationaryJumpSequenceId = 7;
    public const int MovingJumpSequenceId = 8;
    public const int EnvironmentalDeathStartSequenceId = 10;
    public const int EnvironmentalDeathTerminalSequenceId = 11;
    public const int CrouchSequenceId = 13;
    public const int CrouchTurnRightSequenceId = 14;
    public const int CrouchTurnLeftSequenceId = 15;
    public const int LocomotionStopMiddleSequenceId = 20;
    public const int WrenchAttackSequenceId = 23;
    public const int FirstRangedFireSequenceId = 44;

    public const int NeutralActionState = 0;
    public const int CrouchActionState = 4;
    public const int JumpActionState = 7;
    public const int WrenchActionState = 0x13;

    public const int VeldinEnvironmentalDeathTerminalNativeState = 0x77;

    private static readonly int[] neutralIdleCycle =
        [StandingSequenceId, IdleFidgetBSequenceId, StandingSequenceId, IdleFidgetASequenceId];
    private static readonly int[] locomotionEntry =
        [LocomotionStartSequenceId, SustainedLocomotionSequenceId];
    private static readonly int[] locomotionStopFamily =
        [LocomotionStopEarlySequenceId, LocomotionStopMiddleSequenceId, LocomotionStopLateSequenceId];
    private static readonly int[] environmentalDeathPath =
        [EnvironmentalDeathStartSequenceId, EnvironmentalDeathTerminalSequenceId];

    /// <summary>
    /// Untouched neutral Veldin witness. The delay/choice predicate that advances
    /// the fidgets is not yet recovered, so callers must not treat this as a timer.
    /// </summary>
    public static IReadOnlyList<int> NeutralIdleCycle => neutralIdleCycle;

    public static IReadOnlyList<int> LocomotionEntry => locomotionEntry;

    /// <summary>
    /// All three proven release/settle selectors. The default action-3 path
    /// chooses 5/6 by animation time; 20 has a separate release admission path.
    /// </summary>
    public static IReadOnlyList<int> LocomotionStopFamily => locomotionStopFamily;

    /// <summary>
    /// Default mode-0 action-3 selector (Veldin 0x2237c8), after caller guards.
    /// Inputs are Moby+0x52 and evaluated animation time P+0xaa8, not the selected
    /// sequence or an integer frame index. Does not decide entry to action 3 or
    /// the separate caller-selected sequence-20 release path.
    /// </summary>
    public static int SelectDefaultStopSequence(int previousPoseSequence, float animationTime)
    {
        if ((uint)previousPoseSequence > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(previousPoseSequence));
        if (!float.IsFinite(animationTime))
            throw new ArgumentOutOfRangeException(nameof(animationTime));
        return previousPoseSequence == 4 && animationTime > 3f && animationTime < 16f
            ? LocomotionStopEarlySequenceId : LocomotionStopLateSequenceId;
    }

    public static IReadOnlyList<int> EnvironmentalDeathSequencePath => environmentalDeathPath;

    public static int SelectJumpSequence(bool movingAtLaunch) =>
        movingAtLaunch ? MovingJumpSequenceId : StationaryJumpSequenceId;

    public static int SelectCrouchSequence(CrouchTurnDirection turnDirection) =>
        turnDirection switch
        {
            CrouchTurnDirection.None => CrouchSequenceId,
            CrouchTurnDirection.Right => CrouchTurnRightSequenceId,
            CrouchTurnDirection.Left => CrouchTurnLeftSequenceId,
            _ => throw new ArgumentOutOfRangeException(nameof(turnDirection)),
        };

    public static int? SelectFirstRangedFireSequence(bool fireAccepted) =>
        fireAccepted ? FirstRangedFireSequenceId : null;

    /// <summary>
    /// No class-0 hit-reaction sequence has been retained with enough evidence to
    /// promote. The one-Nanotech class-749 consequence is proven independently.
    /// </summary>
    public static int? DamageReactionSequenceId => null;

    /// <summary>
    /// No intermediate selector was witnessed between jump admission and the
    /// launch-context sequence 7/8. That selected sequence then remains active
    /// across the observed rise/apex/fall interval. Landing returns directly to
    /// standing or sustained locomotion rather than selecting a proven land clip.
    /// </summary>
    public static bool HasDistinctJumpAnticipationSequence => false;
    public static bool HasDistinctApexOrFallSequence => false;
    public static bool HasDistinctLandingSequence => false;

    /// <summary>
    /// Zero Nanotech is a proven combat-death boundary, but no class-0 combat
    /// death presentation sequence has been retained. Do not alias environmental
    /// death sequence 10/11 onto combat death without a separate witness.
    /// </summary>
    public static int? CombatDeathSequenceId => null;

    /// <summary>
    /// Ordinary left/right movement uses the same 3 -> 4 selector family.
    /// No separate standing turn-in-place sequence was witnessed.
    /// </summary>
    public static bool OrdinaryTurningUsesLocomotionFamily => true;

    public enum CrouchTurnDirection
    {
        None,
        Left,
        Right,
    }
}
