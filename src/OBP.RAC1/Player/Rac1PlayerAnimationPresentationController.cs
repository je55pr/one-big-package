using OBP.Runtime.Player;

namespace OBP.RAC1.Player;

/// <summary>
/// R&C1-owned presentation selector for the recovered class-0 Ratchet rules.
/// Native sequence and action-state keys remain diagnostic metadata on the
/// engine-neutral decision passed to presentation.
/// </summary>
public sealed class Rac1PlayerAnimationPresentationController : IPlayerAnimationPresentationController
{
    private readonly PlayerAvatar _avatar;
    private double _clockSeconds;
    private PlayerAnimationState _airborneReturnState = PlayerAnimationState.Idle;
    private PlayerAnimationState _attackReturnState = PlayerAnimationState.Idle;
    private int _currentSequenceId = Rac1RatchetSequenceSelection.StandingSequenceId;

    public Rac1PlayerAnimationPresentationController(PlayerAvatar avatar)
    {
        _avatar = avatar;
        Current = Presentation(
            PlayerAnimationState.Idle,
            Rac1RatchetSequenceSelection.StandingSequenceId,
            Rac1RatchetSequenceSelection.NeutralActionState,
            loop: true,
            started: 0);
    }

    public PlayerAnimationPresentation Current { get; private set; }
    public PlayerAnimationPresentation SetClock(double clockSeconds)
    {
        if (!double.IsFinite(clockSeconds))
            throw new ArgumentOutOfRangeException(nameof(clockSeconds));

        _clockSeconds = Math.Max(0, clockSeconds);
        CompleteLocomotionStartIfNeeded();
        CompleteAttackIfNeeded();
        return Current;
    }

    public PlayerAnimationPresentation SetAnimationState(PlayerAnimationState state)
    {
        if (state == Current.SemanticState)
            return Current;

        if (Current.SemanticState == PlayerAnimationState.Attack)
        {
            _attackReturnState = GroundContext(state);
            return Current;
        }

        if (_currentSequenceId == Rac1RatchetSequenceSelection.LocomotionStartSequenceId &&
            IsLocomotion(state))
        {
            Current = Current with { SemanticState = state };
            _airborneReturnState = state;
            return Current;
        }
        PlayerAnimationState previous = Current.SemanticState;
        switch (state)
        {
            case PlayerAnimationState.Idle:
                _airborneReturnState = PlayerAnimationState.Idle;
                Select(
                    state,
                    Rac1RatchetSequenceSelection.StandingSequenceId,
                    Rac1RatchetSequenceSelection.NeutralActionState,
                    loop: true,
                    restart: _currentSequenceId != Rac1RatchetSequenceSelection.StandingSequenceId);
                break;

            case PlayerAnimationState.Walk:
            case PlayerAnimationState.Run:
                _airborneReturnState = state;
                bool startFromIdle = previous == PlayerAnimationState.Idle &&
                                     _currentSequenceId == Rac1RatchetSequenceSelection.StandingSequenceId;
                Select(
                    state,
                    startFromIdle
                        ? Rac1RatchetSequenceSelection.LocomotionStartSequenceId
                        : Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId,
                    Rac1RatchetSequenceSelection.NeutralActionState,
                    loop: !startFromIdle,
                    restart: startFromIdle ||
                             _currentSequenceId != Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId);
                break;
            case PlayerAnimationState.JumpRise:
                bool moving = IsLocomotion(previous) ||
                              _currentSequenceId == Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId;
                _airborneReturnState = moving
                    ? LocomotionContext(previous)
                    : PlayerAnimationState.Idle;
                Select(
                    state,
                    Rac1RatchetSequenceSelection.SelectJumpSequence(moving),
                    Rac1RatchetSequenceSelection.JumpActionState,
                    loop: false,
                    restart: true);
                break;

            case PlayerAnimationState.Fall:
                if (_currentSequenceId is Rac1RatchetSequenceSelection.StationaryJumpSequenceId or
                    Rac1RatchetSequenceSelection.MovingJumpSequenceId)
                {
                    Current = Current with { SemanticState = state };
                }
                else
                {
                    bool movingFall = IsLocomotion(previous) ||
                                      _currentSequenceId == Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId;
                    _airborneReturnState = movingFall
                        ? LocomotionContext(previous)
                        : PlayerAnimationState.Idle;
                    Select(
                        state,
                        Rac1RatchetSequenceSelection.SelectJumpSequence(movingFall),
                        Rac1RatchetSequenceSelection.JumpActionState,
                        loop: false,
                        restart: true);
                }
                break;
            case PlayerAnimationState.Land:
                bool returnToLocomotion = IsLocomotion(_airborneReturnState);
                Select(
                    state,
                    returnToLocomotion
                        ? Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId
                        : Rac1RatchetSequenceSelection.StandingSequenceId,
                    Rac1RatchetSequenceSelection.NeutralActionState,
                    loop: true,
                    restart: true);
                break;

            case PlayerAnimationState.Attack:
                _attackReturnState = GroundContext(previous);
                Select(
                    state,
                    Rac1RatchetSequenceSelection.WrenchAttackSequenceId,
                    Rac1RatchetSequenceSelection.WrenchActionState,
                    loop: false,
                    restart: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return Current;
    }

    private void CompleteLocomotionStartIfNeeded()
    {
        if (!IsLocomotion(Current.SemanticState) ||
            _currentSequenceId != Rac1RatchetSequenceSelection.LocomotionStartSequenceId ||
            Current.ElapsedAt(_clockSeconds) < CurrentClip.DurationSeconds)
        {
            return;
        }

        double completedAt = Current.ClipStartedAtSeconds + CurrentClip.DurationSeconds;
        Select(
            Current.SemanticState,
            Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId,
            Rac1RatchetSequenceSelection.NeutralActionState,
            loop: true,
            restart: true,
            startAtSeconds: completedAt);
    }

    private void CompleteAttackIfNeeded()
    {
        if (Current.SemanticState != PlayerAnimationState.Attack ||
            Current.ElapsedAt(_clockSeconds) < CurrentClip.DurationSeconds)
        {
            return;
        }

        double completedAt = Current.ClipStartedAtSeconds + CurrentClip.DurationSeconds;
        Select(
            _attackReturnState,
            IsLocomotion(_attackReturnState)
                ? Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId
                : Rac1RatchetSequenceSelection.StandingSequenceId,
            Rac1RatchetSequenceSelection.NeutralActionState,
            loop: true,
            restart: true,
            startAtSeconds: completedAt);
    }

    private PlayerAnimationState GroundContext(PlayerAnimationState previous)
    {
        if (IsLocomotion(previous))
            return previous;

        if (_currentSequenceId == Rac1RatchetSequenceSelection.SustainedLocomotionSequenceId &&
            IsLocomotion(_airborneReturnState))
        {
            return _airborneReturnState;
        }

        return PlayerAnimationState.Idle;
    }
    private PlayerAnimationState LocomotionContext(PlayerAnimationState previous) =>
        IsLocomotion(previous)
            ? previous
            : IsLocomotion(_airborneReturnState)
                ? _airborneReturnState
                : PlayerAnimationState.Run;

    private PlayerAvatarAnimationClip CurrentClip =>
        _avatar.AnimationClips.Single(
            clip => string.Equals(clip.Id, Current.ClipId, StringComparison.Ordinal));

    private void Select(
        PlayerAnimationState state,
        int sequenceId,
        int nativeState,
        bool loop,
        bool restart,
        double? startAtSeconds = null)
    {
        string clipId = Rac1PlayerAvatarProvider.AnimationDescriptor(sequenceId).Id;
        double started = restart || !string.Equals(Current.ClipId, clipId, StringComparison.Ordinal)
            ? startAtSeconds ?? _clockSeconds
            : Current.ClipStartedAtSeconds;
        _currentSequenceId = sequenceId;
        Current = Presentation(state, sequenceId, nativeState, loop, started);
    }
    private PlayerAnimationPresentation Presentation(
        PlayerAnimationState state,
        int sequenceId,
        int nativeState,
        bool loop,
        double started)
    {
        string clipId = Rac1PlayerAvatarProvider.AnimationDescriptor(sequenceId).Id;
        return new PlayerAnimationPresentation(
            state,
            clipId,
            started,
            loop,
            SourceSequenceKey: sequenceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SourceStateKey: nativeState.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool IsLocomotion(PlayerAnimationState state) =>
        state is PlayerAnimationState.Walk or PlayerAnimationState.Run;
}
