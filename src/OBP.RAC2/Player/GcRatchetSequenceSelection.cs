namespace OBP.RAC2.Player;

/// <summary>
/// Retail-backed Going Commando Ratchet sequence-selection facts for ordinary
/// player states. Sequence ids here come from the GC state setter and its
/// dedicated 256-slot Ratchet sequence table; no R&amp;C1 sequence role is
/// transferred by analogy.
/// </summary>
public static class GcRatchetSequenceSelection
{
    public const int IdleState = 0;
    public const int WalkState = 2;
    public const int SkidState = 3;
    public const int CrouchState = 4;
    public const int FallState = 6;
    public const int JumpState = 7;
    public const int GlideState = 8;
    public const int ComboAttackState = 19;
    public const int JumpAttackState = 20;
    public const int ThrowAttackState = 21;
    public const int GetHitState = 22;
    public const int TargetingState = 29;
    public const int GunWaitingState = 30;
    public const int DeathState = 57;

    /// <summary>
    /// State 0 passes the current weapon-context selector result to the native
    /// animation setter. With no special weapon context that helper falls back
    /// to sequence 0; equipped weapons may provide a different base sequence.
    /// </summary>
    public const int DefaultIdleSequenceId = 0;

    /// <summary>
    /// State 2 enters ordinary walk on sequence 3. No later fixed sustained-walk
    /// sequence has been recovered from the state-2 handler, so callers must not
    /// alias R&amp;C1 sequence 4 into GC.
    /// </summary>
    public const int WalkEntrySequenceId = 3;

    public const int SkidMidSpeedSequenceId = 5;
    public const int SkidDefaultSequenceId = 6;

    /// <summary>
    /// Crouch entry can select sequence 13 when the transition source agrees with
    /// the current weapon-context base sequence. Directional crouch movement then
    /// selects 14/15 from the sign of native movement scalar +0x1A4.
    /// </summary>
    public const int CrouchSequenceId = 13;
    public const int CrouchNonNegativeDirectionSequenceId = 14;
    public const int CrouchNegativeDirectionSequenceId = 15;

    public const int FallLowSequenceId = 10;
    public const int FallHighSequenceId = 11;
    public const float FallSequenceThreshold = 1.75f;

    public const int GlideSequenceId = 19;

    public const int ComboAttackFirstSequenceId = 23;
    public const int ComboAttackSecondSequenceId = 24;
    public const int ComboAttackThirdSequenceId = 25;
    public const int JumpAttackSequenceId = 43;
    public const int ThrowAttackSequenceId = 26;
    public const int GetHitSequenceId = 16;
    public const int DeathSequenceId = 69;

    /// <summary>
    /// Retail state 3 selects sequence 5 only when the recovered mode helper
    /// returns 4 and native scalar +0xC38 is strictly between 3 and 16.
    /// Otherwise it selects sequence 6.
    /// </summary>
    public static int SelectSkidSequence(bool modeIsFour, float nativeC38) =>
        modeIsFour && nativeC38 > 3f && nativeC38 < 16f
            ? SkidMidSpeedSequenceId
            : SkidDefaultSequenceId;

    /// <summary>
    /// Retail state 4 reaches this directional selector only after its movement
    /// activation predicate has fired. Negative +0x1A4 chooses 15; non-negative
    /// chooses 14.
    /// </summary>
    public static int SelectDirectionalCrouchSequence(float nativeAxis1A4) =>
        nativeAxis1A4 < 0f
            ? CrouchNegativeDirectionSequenceId
            : CrouchNonNegativeDirectionSequenceId;

    /// <summary>
    /// Retail state 6 compares native player scalar +0x31C strictly against
    /// 1.75. Values above it select 11; all other values select 10.
    /// </summary>
    public static int SelectFallSequence(float native31C) =>
        native31C > FallSequenceThreshold
            ? FallHighSequenceId
            : FallLowSequenceId;

    /// <summary>
    /// Retail state 19 derives a modulo-three combo stage and selects 23 + stage.
    /// </summary>
    public static int SelectComboAttackSequence(int comboStage) =>
        comboStage switch
        {
            0 => ComboAttackFirstSequenceId,
            1 => ComboAttackSecondSequenceId,
            2 => ComboAttackThirdSequenceId,
            _ => throw new ArgumentOutOfRangeException(nameof(comboStage)),
        };

    /// <summary>
    /// The state-2 entry selector is proven, but a distinct fixed sustained-walk
    /// selector is not. In particular, sequence 4 must not be imported from R&amp;C1.
    /// </summary>
    public static int? SustainedWalkSequenceId => null;

    /// <summary>
    /// The ordinary jump-family initializer shared by states 7 and several jump
    /// variants does not call the recovered sequence setter directly. The exact
    /// jump-launch sequence remains unresolved.
    /// </summary>
    public static int? JumpLaunchSequenceId => null;

    /// <summary>
    /// State 29 (targeting) re-selects the current weapon-context base sequence
    /// on entry. Its shared movement handler has no recovered fixed directional
    /// sequence write for targeting, so no dedicated strafe sequence is promoted.
    /// </summary>
    public static int? TargetingDirectionalSequenceId => null;

    /// <summary>
    /// State 30 (gun waiting) also obtains its entry sequence through the
    /// weapon-context selector rather than a fixed GC sequence id.
    /// </summary>
    public static int? GunWaitingSequenceId => null;
}
