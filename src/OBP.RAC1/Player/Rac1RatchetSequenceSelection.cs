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
    /// All three proven release/settle selectors. Retail chooses among them from
    /// locomotion-cycle phase. Exact sub-frame boundaries remain unresolved.
    /// </summary>
    public static IReadOnlyList<int> LocomotionStopFamily => locomotionStopFamily;

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
